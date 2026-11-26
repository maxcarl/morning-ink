using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>EF Core access for publishing. Loaded objects are tracked, so changes to them are saved.</summary>
internal sealed class PublishingStore(MorningInkDbContext db, TimeProvider time) : IPublishingStore
{
    private const string FallbackLanguage = "en";

    // Feedback counts this long, so a taste can change again.
    private static readonly TimeSpan FeedbackWindow = TimeSpan.FromDays(60);

    // The newest answers the AI gets as examples of what the reader likes and does not; more make the request long for little gain.
    private const int FeedbackExamples = 10;

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
        var since = time.GetUtcNow() - FeedbackWindow;
        var feedback = await db.Feedback
            .Where(f => f.ProfileId == profileId && f.At >= since)
            .OrderByDescending(f => f.At)
            .Select(f => new { f.SourceId, f.Kind, f.ArticleTitle })
            .ToListAsync(cancellationToken);
        return new PublishingProfile(profile, language ?? FallbackLanguage)
        {
            // Per source: one up for every "more", one down for every "less", within the window.
            SourceLeanings = feedback
                .Where(f => f.SourceId is not null)
                .GroupBy(f => f.SourceId!.Value) // ! is safe: filtered just above
                .ToDictionary(g => g.Key, g => g.Sum(f => f.Kind == FeedbackKind.More ? 1 : -1)),
            LikedTitles = [.. feedback.Where(f => f.Kind == FeedbackKind.More).Select(f => f.ArticleTitle).Take(FeedbackExamples)],
            DislikedTitles = [.. feedback.Where(f => f.Kind == FeedbackKind.Less).Select(f => f.ArticleTitle).Take(FeedbackExamples)],
        };
    }

    public async Task<IReadOnlyList<ProfileSchedule>> ListSchedulesAsync(CancellationToken cancellationToken) =>
        await db.Profiles.Select(p => new ProfileSchedule(p.Id, p.DeliveryTime, p.TimeZoneId, p.Days, p.LeadTime)).ToListAsync(cancellationToken);

    public Task<Issue?> FindIssueAsync(Guid profileId, DateTimeOffset scheduledFor, CancellationToken cancellationToken) =>
        db.Issues
            .Include(i => i.Entries)
            .Include(i => i.Attempts)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.ProfileId == profileId && i.ScheduledFor == scheduledFor, cancellationToken);

    public async Task<IReadOnlyList<Issue>> ListBuildingIssuesAsync(Guid profileId, CancellationToken cancellationToken) =>
        await db.Issues.Where(i => i.ProfileId == profileId && i.Status == IssueStatus.Building).ToListAsync(cancellationToken);

    public async Task<int> NextIssueNumberAsync(Guid profileId, CancellationToken cancellationToken) =>
        (await db.Issues.Where(i => i.ProfileId == profileId).MaxAsync(i => (int?)i.Number, cancellationToken) ?? 0) + 1;

    public void AddIssue(Issue issue) => db.Issues.Add(issue);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
