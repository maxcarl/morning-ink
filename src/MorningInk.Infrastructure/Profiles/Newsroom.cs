using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Scheduling;
using Quartz;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>One profile ("edition") on the dashboard.</summary>
/// <param name="NextIssue">When its next scheduled issue comes, in UTC.</param>
/// <param name="NextNumber">The number that issue gets.</param>
public sealed record Edition(Profile Profile, DateTimeOffset NextIssue, int NextNumber)
{
    /// <summary>Without a recipient or a section, an issue cannot be built and sent.</summary>
    public bool CanSend => CanSendIssues(Profile);

    internal static bool CanSendIssues(Profile profile) => profile.IsReady;
}

/// <summary>What the dashboard shows.</summary>
/// <param name="Editions">The user's profiles, the one with the next issue first.</param>
/// <param name="Issues">The latest issues of all of them, newest first, with their delivery attempts and entries.</param>
/// <param name="MailServerSet">Whether a mail server is configured; without one nothing can be delivered.</param>
/// <param name="AilingSources">Names of the user's sources failing or empty for days, see <see cref="SourceHealth"/>.</param>
public sealed record NewsroomView(IReadOnlyList<Edition> Editions, IReadOnlyList<Issue> Issues, bool MailServerSet, IReadOnlyList<string> AilingSources);

/// <summary>The dashboard's data and its actions, "send now" and cancelling an issue being built.</summary>
public sealed class Newsroom(
    MorningInkDbContext db,
    ProfileEditor profiles,
    MailSettingsStore mail,
    ISchedulerFactory schedulers,
    IssueScheduler issues,
    IssuePublisher publisher,
    TimeProvider time)
{
    private const int RecentIssues = 10;

    public async Task<NewsroomView> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var all = await profiles.ListAsync(userId, cancellationToken);
        var ids = all.Select(p => p.Id).ToList();
        var lastNumbers = await db.Issues
            .Where(i => ids.Contains(i.ProfileId))
            .GroupBy(i => i.ProfileId)
            .Select(g => new { ProfileId = g.Key, Number = g.Max(i => i.Number) })
            .ToDictionaryAsync(x => x.ProfileId, x => x.Number, cancellationToken);
        var now = time.GetUtcNow();
        var editions = all
            .Select(p => new Edition(
                p,
                // Saved crontab expressions always fire again; the fallback only sorts a broken one last.
                IssueScheduler.NextSlot(ProfileSchedule.Of(p), now) ?? DateTimeOffset.MaxValue,
                lastNumbers.GetValueOrDefault(p.Id) + 1))
            .OrderBy(e => e.NextIssue)
            .ToList();
        var recent = await db.Issues.AsNoTracking()
            .Where(i => ids.Contains(i.ProfileId))
            .OrderByDescending(i => i.ScheduledFor)
            .Take(RecentIssues)
            .Include(i => i.Attempts)
            .Include(i => i.Entries)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var mailServerSet = !string.IsNullOrWhiteSpace((await mail.EffectiveAsync(cancellationToken)).Host);
        var troubled = await db.Sources.AsNoTracking()
            .Where(s => s.UserId == userId && (s.FailingSince != null || s.EmptySince != null))
            .ToListAsync(cancellationToken);
        var ailing = troubled
            .Where(s => s.Health(now) is SourceHealth.Failing { Warning: true } or SourceHealth.Empty { Warning: true })
            .Select(s => s.Name)
            .Order(StringComparer.CurrentCulture)
            .ToList();
        return new NewsroomView(editions, recent, mailServerSet, ailing);
    }

    /// <summary>
    /// The status of the user's newest issue created since the given time; null while none is there yet. Cheap enough
    /// to ask every few seconds while the page follows an issue it started.
    /// </summary>
    public async Task<IssueStatus?> LatestStatusAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken) =>
        await db.Issues.AsNoTracking()
            .Where(i => i.CreatedAt >= since && db.Profiles.Any(p => p.Id == i.ProfileId && p.UserId == userId))
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => (IssueStatus?)i.Status)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Starts building and sending an issue of the profile in the background; it then shows up in <see cref="LoadAsync"/>.</summary>
    /// <returns>False when the profile is not the user's or cannot be sent yet, see <see cref="Edition.CanSend"/>.</returns>
    public async Task<bool> SendNowAsync(Guid userId, Guid profileId, CancellationToken cancellationToken)
    {
        if (await profiles.FindAsync(userId, profileId, cancellationToken) is not { } profile || !Edition.CanSendIssues(profile))
        {
            return false;
        }

        // Through Quartz, not in the page: the run goes on when the tab closes, and failed deliveries are retried.
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        await issues.SendNowAsync(scheduler, ProfileSchedule.Of(profile), cancellationToken);
        return true;
    }

    /// <summary>Cancels the issue the user's profile is building, see <see cref="IssuePublisher.CancelAsync"/>.</summary>
    /// <returns>False when the profile is not the user's or nothing was being built.</returns>
    public async Task<bool> CancelAsync(Guid userId, Guid profileId, CancellationToken cancellationToken) =>
        await profiles.FindAsync(userId, profileId, cancellationToken) is not null
        && await publisher.CancelAsync(profileId, cancellationToken);
}
