using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Application.Scheduling;
using MorningInk.Domain;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Scheduling;
using Quartz;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>What the dashboard shows.</summary>
/// <param name="Issues">The latest issues, newest first, with their delivery attempts and entries.</param>
/// <param name="NextIssue">When the next scheduled issue comes, in UTC.</param>
/// <param name="MailServerSet">Whether a mail server is configured; without one nothing can be delivered.</param>
public sealed record NewsroomView(Profile Profile, IReadOnlyList<Issue> Issues, DateTimeOffset NextIssue, bool MailServerSet)
{
    /// <summary>Without a recipient or a section, an issue cannot be built and sent.</summary>
    public bool CanSend => CanSendIssues(Profile);

    internal static bool CanSendIssues(Profile profile) => !string.IsNullOrEmpty(profile.Recipient) && profile.Sections.Count > 0;
}

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
        var profile = await profiles.GetOrCreateAsync(userId, cancellationToken);
        var recent = await db.Issues.AsNoTracking()
            .Where(i => i.ProfileId == profile.Id)
            .OrderByDescending(i => i.ScheduledFor)
            .Take(RecentIssues)
            .Include(i => i.Attempts)
            .Include(i => i.Entries)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var next = DeliverySlots.NextAfter(time.GetUtcNow(), profile.DeliveryTime, ProfileTimeZone.Find(profile.TimeZoneId));
        var mailServerSet = !string.IsNullOrWhiteSpace((await mail.EffectiveAsync(cancellationToken)).Host);
        return new NewsroomView(profile, recent, next, mailServerSet);
    }

    /// <summary>Starts building and sending an issue in the background; it then shows up in <see cref="LoadAsync"/>.</summary>
    /// <returns>False when the profile cannot be sent yet, see <see cref="NewsroomView.CanSend"/>.</returns>
    public async Task<bool> SendNowAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetOrCreateAsync(userId, cancellationToken);
        if (!NewsroomView.CanSendIssues(profile))
        {
            return false;
        }

        // Through Quartz, not in the page: the run goes on when the tab closes, and failed deliveries are retried.
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        await issues.SendNowAsync(scheduler, new ProfileSchedule(profile.Id, profile.DeliveryTime, profile.TimeZoneId), cancellationToken);
        return true;
    }
}
