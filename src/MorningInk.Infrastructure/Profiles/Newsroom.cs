using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Application.Scheduling;
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

    internal static bool CanSendIssues(Profile profile) => !string.IsNullOrEmpty(profile.Recipient) && profile.Sections.Count > 0;
}

/// <summary>What the dashboard shows.</summary>
/// <param name="Editions">The user's profiles, the one with the next issue first.</param>
/// <param name="Issues">The latest issues of all of them, newest first, with their delivery attempts and entries.</param>
/// <param name="MailServerSet">Whether a mail server is configured; without one nothing can be delivered.</param>
public sealed record NewsroomView(IReadOnlyList<Edition> Editions, IReadOnlyList<Issue> Issues, bool MailServerSet);

/// <summary>The dashboard's data and its one action, "send now".</summary>
public sealed class Newsroom(
    MorningInkDbContext db,
    ProfileEditor profiles,
    MailSettingsStore mail,
    ISchedulerFactory schedulers,
    IssueScheduler issues,
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
                DeliverySlots.NextAfter(now, p.DeliveryTime, ProfileTimeZone.Find(p.TimeZoneId), p.Days),
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
        return new NewsroomView(editions, recent, mailServerSet);
    }

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
        await issues.SendNowAsync(scheduler, new ProfileSchedule(profile.Id, profile.DeliveryTime, profile.TimeZoneId, profile.Days), cancellationToken);
        return true;
    }
}
