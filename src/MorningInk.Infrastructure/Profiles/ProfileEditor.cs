using System.Globalization;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Application.Scheduling;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Scheduling;
using Quartz;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>What the profile page saves; times and zones as typed, validated by <see cref="ProfileEditor"/>.</summary>
/// <param name="DeliveryTime">"HH:mm", e.g. "06:00".</param>
/// <param name="TimeZoneId">IANA id, e.g. "Europe/Berlin".</param>
/// <param name="Country">ISO code, e.g. "DE"; empty searches worldwide.</param>
/// <param name="Days">Weekdays with an issue; null keeps the current ones, all seven mean every day, none is an error.</param>
/// <param name="LeadTime">How long before delivery to build the issue, up to half a day; null keeps the current one.</param>
/// <param name="SendByEmail">Off, issues are only stored to fetch, and the recipient may stay empty.</param>
public sealed record ProfileSettings(
    string Title,
    string Recipient,
    string DeliveryTime,
    string TimeZoneId,
    string? Region,
    string? Country = null,
    IReadOnlyCollection<DayOfWeek>? Days = null,
    TimeSpan? LeadTime = null,
    bool SendByEmail = true);

/// <summary>Where an edition announces its issues; see <see cref="Profile.NotifyChannel"/>.</summary>
/// <param name="Secret">Token for the channel; empty keeps the saved one, as with the mail password.</param>
public sealed record NotificationSettings(NotificationChannel Channel, string? Target, string? Secret);

/// <summary>
/// Edits a user's issue profiles ("editions", e.g. morning and evening) and their sections. Every method only reaches the
/// given user's profiles, and sections only ever get that user's sources. Schedule changes take effect with the next issue.
/// </summary>
public sealed partial class ProfileEditor(MorningInkDbContext db, ISchedulerFactory schedulers, IssueScheduler issueScheduler, IIssueFiles files)
{
    private const string DefaultTitle = "Morning Ink";
    private const int MaxNameLength = 200;
    private const int MaxRegionLength = 100;
    private const int MaxSectionArticles = 100;
    private const string TimeFormat = "HH:mm";
    private static readonly TimeSpan MaxLeadTime = TimeSpan.FromHours(12);
    private const int MaxInterests = 30;
    private const int MaxKeywords = 30;

    /// <summary>The user's first profile with sections and sources; created with defaults when the user has none.</summary>
    public async Task<Profile> GetOrCreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Ids are version 7 GUIDs, so the oldest profile comes first.
        var profile = await Query(userId).OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = new Profile { UserId = userId, Title = DefaultTitle, Recipient = string.Empty };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        await RescheduleAsync(profile, cancellationToken);
        return profile;
    }

    /// <summary>All profiles of the user, oldest first.</summary>
    public async Task<IReadOnlyList<Profile>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        await GetOrCreateAsync(userId, cancellationToken);
        return await Query(userId).OrderBy(p => p.Id).ToListAsync(cancellationToken);
    }

    /// <summary>The profile if it is the user's; null otherwise.</summary>
    public Task<Profile?> FindAsync(Guid userId, Guid profileId, CancellationToken cancellationToken) =>
        Query(userId).SingleOrDefaultAsync(p => p.Id == profileId, cancellationToken);

    /// <summary>Adds a profile. It starts with the recipient, time and region of the first one, so a second edition needs few changes.</summary>
    public async Task<Profile> CreateAsync(Guid userId, string title, CancellationToken cancellationToken)
    {
        var first = await GetOrCreateAsync(userId, cancellationToken);
        title = title.Trim();
        var profile = new Profile
        {
            UserId = userId,
            Title = title.Length is 0 or > MaxNameLength ? DefaultTitle : title,
            Recipient = first.Recipient,
            DeliveryTime = first.DeliveryTime,
            TimeZoneId = first.TimeZoneId,
            Region = first.Region,
            Country = first.Country,
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        await RescheduleAsync(profile, cancellationToken);
        return profile;
    }

    /// <summary>Deletes the profile with its sections, issues and their files, and stops its schedule.</summary>
    /// <returns>False when it is not the user's, or the last profile they have: a user always keeps one.</returns>
    public async Task<bool> DeleteAsync(Guid userId, Guid profileId, CancellationToken cancellationToken)
    {
        var ids = await db.Profiles.Where(p => p.UserId == userId).Select(p => p.Id).ToListAsync(cancellationToken);
        if (!ids.Contains(profileId) || ids.Count == 1)
        {
            return false;
        }

        var fileNames = await db.Issues.Where(i => i.ProfileId == profileId && i.FileName != null).Select(i => i.FileName!).ToListAsync(cancellationToken);
        await db.Profiles.Where(p => p.Id == profileId).ExecuteDeleteAsync(cancellationToken); // sections and issues go with it (cascade)
        foreach (var path in fileNames.Select(files.PathFor).Where(File.Exists))
        {
            File.Delete(path);
        }

        await (await schedulers.GetScheduler(cancellationToken)).DeleteJob(IssueScheduler.JobKeyFor(profileId), cancellationToken);
        return true;
    }

    /// <summary>Saves the settings, or returns the names of the invalid fields: Title, Recipient, DeliveryTime, TimeZone, Region, Country, Days, LeadTime, Profile.</summary>
    public async Task<IReadOnlyList<string>> SaveSettingsAsync(Guid userId, Guid profileId, ProfileSettings settings, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var title = settings.Title.Trim();
        var recipient = settings.Recipient.Trim();
        var region = settings.Region?.Trim();

        if (title.Length is 0 or > MaxNameLength)
        {
            errors.Add("Title");
        }

        if ((settings.SendByEmail || recipient.Length > 0) && !IsSingleAddress(recipient))
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

        if (!Countries.TryNormalize(settings.Country, out var country))
        {
            errors.Add("Country");
        }

        if (settings.Days is { Count: 0 })
        {
            errors.Add("Days");
        }

        if (settings.LeadTime is { } lead && (lead < TimeSpan.Zero || lead > MaxLeadTime))
        {
            errors.Add("LeadTime");
        }

        var profile = await FindAsync(userId, profileId, cancellationToken);
        if (profile is null)
        {
            errors.Add("Profile");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        // ! is safe: a missing profile returned above
        profile!.Title = title;
        profile.Recipient = recipient;
        profile.SendByEmail = settings.SendByEmail;
        profile.DeliveryTime = time;
        profile.TimeZoneId = settings.TimeZoneId;
        profile.Region = string.IsNullOrEmpty(region) ? null : region;
        profile.Country = country;
        if (settings.LeadTime is { } leadTime)
        {
            profile.LeadTime = leadTime;
        }

        if (settings.Days is { } days)
        {
            // Stored as "every day" when all are ticked, so later changes to the default reach these profiles too.
            profile.Days = DeliverySlots.IsEveryDay(days) ? [] : [.. days.Distinct().Order()];
        }

        await db.SaveChangesAsync(cancellationToken);
        await RescheduleAsync(profile, cancellationToken);
        return [];
    }

    /// <summary>Replaces the edition's interests.</summary>
    /// <returns>False when the edition is not the user's, or an interest lacks a topic or keywords or is too long.</returns>
    public async Task<bool> SaveInterestsAsync(Guid userId, Guid profileId, IReadOnlyList<Interest> interests, CancellationToken cancellationToken)
    {
        var valid = interests.Count <= MaxInterests && interests.All(i =>
            i.Topic.Trim().Length is > 0 and <= MaxNameLength
            && i.Keywords.Count is > 0 and <= MaxKeywords
            && i.Keywords.All(k => k.Trim().Length is > 0 and <= MaxNameLength)
            && Enum.IsDefined(i.Weight));
        if (!valid || await FindAsync(userId, profileId, cancellationToken) is not { } profile)
        {
            return false;
        }

        profile.Interests = interests.Select(i => new Interest { Topic = i.Topic.Trim(), Keywords = [.. i.Keywords.Select(k => k.Trim())], Weight = i.Weight }).ToList();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Sets where the edition announces its issues, or returns the invalid fields: NotifyTarget, NotifySecret, Profile.</summary>
    public async Task<IReadOnlyList<string>> SaveNotificationAsync(Guid userId, Guid profileId, NotificationSettings settings, CancellationToken cancellationToken)
    {
        if (await FindAsync(userId, profileId, cancellationToken) is not { } profile)
        {
            return ["Profile"];
        }

        var target = settings.Target?.Trim() ?? string.Empty;
        var secret = string.IsNullOrWhiteSpace(settings.Secret) ? null : settings.Secret.Trim();
        var keptSecret = profile.NotifyChannel == settings.Channel ? profile.NotifySecret : null;
        var errors = new List<string>();
        if (settings.Channel != NotificationChannel.None && !IsValidTarget(settings.Channel, target))
        {
            errors.Add("NotifyTarget");
        }

        // Gotify and Telegram cannot work without their token; ntfy only needs one for protected topics.
        if (settings.Channel is NotificationChannel.Gotify or NotificationChannel.Telegram && (secret ?? keptSecret) is null)
        {
            errors.Add("NotifySecret");
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        profile.NotifyChannel = settings.Channel;
        profile.NotifyTarget = settings.Channel == NotificationChannel.None ? null : target;
        profile.NotifySecret = settings.Channel is NotificationChannel.None or NotificationChannel.Email ? null : secret ?? keptSecret;
        await db.SaveChangesAsync(cancellationToken);
        return [];
    }

    public async Task<bool> AddSectionAsync(Guid userId, Guid profileId, string name, CancellationToken cancellationToken)
    {
        name = name.Trim();
        if (name.Length is 0 or > MaxNameLength || await FindAsync(userId, profileId, cancellationToken) is not { } profile)
        {
            return false;
        }

        var position = profile.Sections.Select(s => s.Position).DefaultIfEmpty(0).Max() + 1;
        profile.Sections.Add(new Section { Name = name, Position = position });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <param name="sourceIds">Sources to put into the section; ids that are not the user's own are ignored.</param>
    public async Task<bool> SaveSectionAsync(Guid userId, Guid sectionId, string name, int? maxArticles, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
    {
        name = name.Trim();
        var section = (await OwnerOfSectionAsync(userId, sectionId, cancellationToken))?.Sections.Single(s => s.Id == sectionId);
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
        if (await OwnerOfSectionAsync(userId, sectionId, cancellationToken) is not { } profile)
        {
            return false;
        }

        profile.Sections.Remove(profile.Sections.Single(s => s.Id == sectionId));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Moves the section to the given place in the order (0 is the top), e.g. after drag and drop.</summary>
    /// <remarks>An index outside the list puts it first or last, so a stale page can never lose a section.</remarks>
    public async Task<bool> MoveSectionAsync(Guid userId, Guid sectionId, int index, CancellationToken cancellationToken)
    {
        if (await OwnerOfSectionAsync(userId, sectionId, cancellationToken) is not { } profile)
        {
            return false;
        }

        var ordered = profile.Sections.OrderBy(s => s.Position).ToList();
        var section = ordered.Single(s => s.Id == sectionId);

        ordered.Remove(section);
        ordered.Insert(Math.Clamp(index, 0, ordered.Count), section);

        // Renumbered from 1, so positions stay dense whatever happened before.
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Position = i + 1;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<Profile> Query(Guid userId) =>
        db.Profiles.Where(p => p.UserId == userId).Include(p => p.Sections).ThenInclude(s => s.Sources).AsSplitQuery();

    // The user's profile that holds the section; null when no profile of theirs does.
    private Task<Profile?> OwnerOfSectionAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken) =>
        Query(userId).SingleOrDefaultAsync(p => p.Sections.Any(s => s.Id == sectionId), cancellationToken);

    private static bool IsValidTarget(NotificationChannel channel, string target) => channel switch
    {
        NotificationChannel.Ntfy => IsWebAddress(target, out var topic) && topic.AbsolutePath.Trim('/').Length > 0,
        NotificationChannel.Gotify => IsWebAddress(target, out _),
        NotificationChannel.Telegram => TelegramChat().IsMatch(target),
        NotificationChannel.Email => IsSingleAddress(target),
        _ => false,
    };

    private static bool IsWebAddress(string text, out Uri uri) =>
        Uri.TryCreate(text, UriKind.Absolute, out uri!) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo); // ! is safe: only read when this returns true

    // A numeric chat id (negative for groups) or a public channel's @name, optionally "/thread" for a forum topic.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(-?\d{1,20}|@[A-Za-z0-9_]{5,32})(/\d{1,10})?$", System.Text.RegularExpressions.RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial System.Text.RegularExpressions.Regex TelegramChat();

    // Exactly one plain address: no display name, no list, no line breaks (it ends up in a mail header).
    private static bool IsSingleAddress(string value) =>
        value.Length > 0 && !value.Any(c => char.IsControl(c) || c is ',' or ';' or ' ')
        && MailAddress.TryCreate(value, out var address) && address.Address == value;

    // Without catch-up: saving settings at 7:00 must not send the 6:00 issue.
    private async Task RescheduleAsync(Profile profile, CancellationToken cancellationToken) =>
        await issueScheduler.ScheduleAsync(
            await schedulers.GetScheduler(cancellationToken),
            new ProfileSchedule(profile.Id, profile.DeliveryTime, profile.TimeZoneId, profile.Days, profile.LeadTime),
            cancellationToken,
            catchUp: false);
}
