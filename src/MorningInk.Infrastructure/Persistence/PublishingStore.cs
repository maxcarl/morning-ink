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

        var language = await db.Users.Where(u => u.Id == profile.UserId).Select(u => u.Language).SingleOrDefaultAsync(cancellationToken);
        return new PublishingProfile(profile, language ?? FallbackLanguage);
    }

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
