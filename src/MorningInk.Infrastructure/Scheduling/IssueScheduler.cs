using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningInk.Application.Publishing;
using MorningInk.Application.Scheduling;
using Quartz;

namespace MorningInk.Infrastructure.Scheduling;

/// <summary>
/// Plans issues in Quartz: a daily trigger per profile at its delivery time and time zone, a catch-up run for a
/// slot missed while Morning Ink was off, and retries of failed deliveries.
/// </summary>
/// <remarks>
/// Quartz keeps triggers in memory only. The issue table is the record of what was sent, so after a restart
/// everything is planned again and the last slot is simply run once more; the publisher skips delivered slots.
/// </remarks>
public sealed partial class IssueScheduler(
    IServiceScopeFactory scopes,
    IOptions<SchedulingOptions> options,
    TimeProvider time,
    ILogger<IssueScheduler> logger)
{
    internal const string ProfileIdKey = "profileId";
    internal const string SlotKey = "slotUnixMs";
    private const int RetryGrowthFactor = 3;

    public static JobKey JobKeyFor(Guid profileId) => new($"publish-{profileId}");

    /// <summary>Plans every profile; safe to call again, e.g. after profiles changed.</summary>
    public async Task ScheduleAllAsync(IScheduler scheduler, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProfileSchedule> schedules;
        await using (var scope = scopes.CreateAsyncScope())
        {
            schedules = await scope.ServiceProvider.GetRequiredService<IPublishingStore>().ListSchedulesAsync(cancellationToken);
        }

        foreach (var schedule in schedules)
        {
            await ScheduleAsync(scheduler, schedule, cancellationToken);
        }
    }

    /// <summary>Plans one profile, replacing its previous daily trigger.</summary>
    /// <param name="catchUp">Run a missed slot now; at start yes, after editing a profile no (saving settings must not send an issue).</param>
    public async Task ScheduleAsync(IScheduler scheduler, ProfileSchedule schedule, CancellationToken cancellationToken, bool catchUp = true)
    {
        var zone = ProfileTimeZone.Find(schedule.TimeZoneId);
        var job = JobBuilder.Create<PublishIssueJob>()
            .WithIdentity(JobKeyFor(schedule.ProfileId))
            .UsingJobData(ProfileIdKey, schedule.ProfileId.ToString())
            .StoreDurably()
            .Build();
        await scheduler.AddJob(job, AddJobOptions.Replacing, cancellationToken);

        var daily = TriggerBuilder.Create(time)
            .WithIdentity($"daily-{schedule.ProfileId}")
            .ForJob(job)
            .WithSchedule(CronScheduleBuilder
                .Create(string.Create(CultureInfo.InvariantCulture, $"0 {schedule.DeliveryTime.Minute} {schedule.DeliveryTime.Hour} * * ?"))
                .InTimeZone(zone)

                // If Morning Ink was busy at the moment, run as soon as possible instead of skipping the day.
                .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
            .Build();
        await scheduler.UnscheduleJob(daily.Key, cancellationToken);
        await scheduler.ScheduleJob(daily, default, cancellationToken);

        var now = time.GetUtcNow();
        var lastSlot = DeliverySlots.LastAtOrBefore(now, schedule.DeliveryTime, zone);
        if (catchUp && now - lastSlot <= options.Value.CatchUpWindow)
        {
            // Harmless when the slot was already delivered: the publisher then does nothing.
            await scheduler.ScheduleJob(OneOff(job.Key, $"catch-up-{schedule.ProfileId}", lastSlot, now), ScheduleJobOptions.Replacing, cancellationToken);
        }
    }

    /// <summary>Plans another delivery attempt for a slot, or gives up after the configured number of attempts.</summary>
    /// <param name="attempts">Attempts made so far.</param>
    public async Task ScheduleRetryAsync(IScheduler scheduler, Guid profileId, DateTimeOffset slot, int attempts, CancellationToken cancellationToken)
    {
        if (attempts >= options.Value.MaxDeliveryAttempts)
        {
            LogGaveUp(logger, profileId, attempts);
            return;
        }

        var at = time.GetUtcNow() + RetryDelay(attempts, options.Value.FirstRetryDelay);
        await scheduler.ScheduleJob(OneOff(JobKeyFor(profileId), $"retry-{profileId}-{attempts}", slot, at), ScheduleJobOptions.Replacing, cancellationToken);
        LogRetry(logger, profileId, at);
    }

    /// <summary>5, 15, 45, 135 minutes … with the default first delay.</summary>
    internal static TimeSpan RetryDelay(int attempts, TimeSpan firstDelay) =>
        firstDelay * Math.Pow(RetryGrowthFactor, Math.Max(0, attempts - 1));

    // The slot travels with the trigger, so a late or repeated run still publishes the right issue.
    private ITrigger OneOff(JobKey job, string name, DateTimeOffset slot, DateTimeOffset startAt) =>
        TriggerBuilder.Create(time)
            .WithIdentity(name)
            .ForJob(job)
            .UsingJobData(SlotKey, slot.ToUnixTimeMilliseconds())
            .StartAt(startAt)
            .WithSimpleSchedule(s => s.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
            .Build();

    [LoggerMessage(Level = LogLevel.Information, Message = "Retrying delivery for profile {ProfileId} at {At}")]
    private static partial void LogRetry(ILogger logger, Guid profileId, DateTimeOffset at);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gave up delivering the issue of profile {ProfileId} after {Attempts} attempts")]
    private static partial void LogGaveUp(ILogger logger, Guid profileId, int attempts);
}
