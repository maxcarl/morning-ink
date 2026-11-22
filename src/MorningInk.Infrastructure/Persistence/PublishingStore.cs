using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>EF Core access for publishing. Loaded objects are tracked, so changes to them are saved.</summary>
internal sealed class PublishingStore(MorningInkDbContext db) : IPublishingStore
{
    private const string FallbackLanguage = "en";

    public async Task<PublishingProfile?> LoadProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await db.Profiles
            .Include(p => p.Sections).ThenInclude(s => s.Sources)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == profileId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        // The edition's country first (a paper for Munich speaks German), if issues can be printed in its language;
        // otherwise the owner's language, so texts and dates never come in two languages at once.
        var countryLanguage = Countries.LanguageOf(profile.Country);
        var language = countryLanguage is not null && Rendering.EpubTexts.Exist(countryLanguage)
            ? countryLanguage
            : await db.DomainUsers.Where(u => u.Id == profile.UserId).Select(u => u.Language).SingleOrDefaultAsync(cancellationToken);
        return new PublishingProfile(profile, language ?? FallbackLanguage);
    }

    public async Task<IReadOnlyList<ProfileSchedule>> ListSchedulesAsync(CancellationToken cancellationToken) =>
        await db.Profiles.Select(p => new ProfileSchedule(p.Id, p.DeliveryTime, p.TimeZoneId, p.Days, p.LeadTime)).ToListAsync(cancellationToken);

    public Task<Issue?> FindIssueAsync(Guid profileId, DateTimeOffset scheduledFor, CancellationToken cancellationToken) =>
        db.Issues
            .Include(i => i.Entries)
            .Include(i => i.Attempts)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.ProfileId == profileId && i.ScheduledFor == scheduledFor, cancellationToken);

    public async Task<int> NextIssueNumberAsync(Guid profileId, CancellationToken cancellationToken) =>
        (await db.Issues.Where(i => i.ProfileId == profileId).MaxAsync(i => (int?)i.Number, cancellationToken) ?? 0) + 1;

    public void AddIssue(Issue issue) => db.Issues.Add(issue);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
