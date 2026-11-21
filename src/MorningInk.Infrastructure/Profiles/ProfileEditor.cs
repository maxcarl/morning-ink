using System.Globalization;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Scheduling;
using Quartz;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>What the profile page saves; times and zones as typed, validated by <see cref="ProfileEditor"/>.</summary>
/// <param name="DeliveryTime">"HH:mm", e.g. "06:00".</param>
/// <param name="TimeZoneId">IANA id, e.g. "Europe/Berlin".</param>
public sealed record ProfileSettings(string Title, string Recipient, string DeliveryTime, string TimeZoneId, string? Region);

/// <summary>
/// Edits a user's issue profile and its sections. Every method only reaches the given user's profile, and sections only
/// ever get that user's sources. Changes to the schedule take effect with the next issue.
/// </summary>
public sealed class ProfileEditor(MorningInkDbContext db, ISchedulerFactory schedulers, IssueScheduler issueScheduler)
{
    private const string DefaultTitle = "Morning Ink";
    private const int MaxNameLength = 200;
    private const int MaxRegionLength = 100;
    private const int MaxSectionArticles = 100;
    private const string TimeFormat = "HH:mm";

    /// <summary>The user's profile with sections and sources; created with defaults on the first visit.</summary>
    public async Task<Profile> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles
            .Where(p => p.UserId == userId)
            .Include(p => p.Sections).ThenInclude(s => s.Sources)
            .AsSplitQuery()
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        // ponytail: one profile per user for now; several issues per user (morning, evening) come with stage 2.
        profile = new Profile { UserId = userId, Title = DefaultTitle, Recipient = string.Empty };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        await RescheduleAsync(profile, cancellationToken);
        return profile;
    }

    /// <summary>Saves the settings, or returns the names of the invalid fields: Title, Recipient, DeliveryTime, TimeZone, Region.</summary>
    public async Task<IReadOnlyList<string>> SaveSettingsAsync(Guid userId, ProfileSettings settings, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var title = settings.Title.Trim();
        var recipient = settings.Recipient.Trim();
        var region = settings.Region?.Trim();

        if (title.Length is 0 or > MaxNameLength)
        {
            errors.Add("Title");
        }

        if (!IsSingleAddress(recipient))
        {
            errors.Add("Recipient");
        }

        if (!TimeOnly.TryParseExact(settings.DeliveryTime.Trim(), TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            errors.Add("DeliveryTime");
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(settings.TimeZoneId, out _))
        {
            errors.Add("TimeZone");
        }

        if (region?.Length > MaxRegionLength)
        {
            errors.Add("Region");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var profile = await GetOrCreateAsync(userId, cancellationToken);
        profile.Title = title;
        profile.Recipient = recipient;
        profile.DeliveryTime = time;
        profile.TimeZoneId = settings.TimeZoneId;
        profile.Region = string.IsNullOrEmpty(region) ? null : region;
        await db.SaveChangesAsync(cancellationToken);
        await RescheduleAsync(profile, cancellationToken);
        return [];
    }

    public async Task<bool> AddSectionAsync(Guid userId, string name, CancellationToken cancellationToken)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength)
        {
            return false;
        }

        var profile = await GetOrCreateAsync(userId, cancellationToken);
        var position = profile.Sections.Select(s => s.Position).DefaultIfEmpty(0).Max() + 1;
        profile.Sections.Add(new Section { Name = name, Position = position });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <param name="sourceIds">Sources to put into the section; ids that are not the user's own are ignored.</param>
    public async Task<bool> SaveSectionAsync(Guid userId, Guid sectionId, string name, int? maxArticles, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
    {
        name = name.Trim();
        var section = (await GetOrCreateAsync(userId, cancellationToken)).Sections.SingleOrDefault(s => s.Id == sectionId);
        if (section is null || name.Length is 0 or > MaxNameLength || maxArticles is < 1 or > MaxSectionArticles)
        {
            return false;
        }

        // Only the user's own sources, whatever the form sent.
        var ids = sourceIds.ToList();
        var sources = await db.Sources.Where(s => s.UserId == userId && ids.Contains(s.Id)).ToListAsync(cancellationToken);
        section.Name = name;
        section.MaxArticles = maxArticles;
        section.Sources.Clear();
        sources.ForEach(section.Sources.Add);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteSectionAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken)
    {
        var profile = await GetOrCreateAsync(userId, cancellationToken);
        if (profile.Sections.SingleOrDefault(s => s.Id == sectionId) is not { } section)
        {
            return false;
        }

        profile.Sections.Remove(section);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Swaps the section with its neighbour above or below.</summary>
    public async Task<bool> MoveSectionAsync(Guid userId, Guid sectionId, bool up, CancellationToken cancellationToken)
    {
        var ordered = (await GetOrCreateAsync(userId, cancellationToken)).Sections.OrderBy(s => s.Position).ToList();
        var index = ordered.FindIndex(s => s.Id == sectionId);
        var other = up ? index - 1 : index + 1;
        if (index < 0 || other < 0 || other >= ordered.Count)
        {
            return false;
        }

        (ordered[index].Position, ordered[other].Position) = (ordered[other].Position, ordered[index].Position);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Exactly one plain address: no display name, no list, no line breaks (it ends up in a mail header).
    private static bool IsSingleAddress(string value) =>
        value.Length > 0 && !value.Any(c => char.IsControl(c) || c is ',' or ';' or ' ')
        && MailAddress.TryCreate(value, out var address) && address.Address == value;

    // Without catch-up: saving settings at 7:00 must not send the 6:00 issue.
    private async Task RescheduleAsync(Profile profile, CancellationToken cancellationToken) =>
        await issueScheduler.ScheduleAsync(
            await schedulers.GetScheduler(cancellationToken),
            new ProfileSchedule(profile.Id, profile.DeliveryTime, profile.TimeZoneId),
            cancellationToken,
            catchUp: false);
}
