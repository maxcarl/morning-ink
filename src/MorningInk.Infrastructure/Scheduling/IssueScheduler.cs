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

    /// <summary>On the build trigger: the lead time in minutes; the run builds the issue for its fire time plus this.</summary>
    internal const string LeadMinutesKey = "leadMinutes";

    // Trigger names start with what the trigger is for; the jobs page reads the kind back from them.
    private const string DailyPrefix = "daily-";
    private const string BuildPrefix = "build-";
    private const string CatchUpPrefix = "catch-up-";
    private const string NowPrefix = "now-";
    private const string RetryPrefix = "retry-";
    private const string AfterBuildPrefix = "after-build-";

    private static readonly (string Prefix, JobKind Kind)[] Kinds =
    [
        (DailyPrefix, JobKind.Daily), (BuildPrefix, JobKind.Build), (CatchUpPrefix, JobKind.CatchUp),
        (NowPrefix, JobKind.Now), (RetryPrefix, JobKind.Retry), (AfterBuildPrefix, JobKind.AfterBuild),
    ];

    // How soon a delivery tries again while the early build of the same issue still runs.
    private static readonly TimeSpan BusyRetryDelay = TimeSpan.FromMinutes(2);
    private const int RetryGrowthFactor = 3;

    private const string JobPrefix = "publish-";

    public static JobKey JobKeyFor(Guid profileId) => new($"{JobPrefix}{profileId}");

    /// <summary>The profile an issue job is for; null for other jobs.</summary>
    public static Guid? ProfileOf(JobKey? job) =>
        job is not null && job.Name.StartsWith(JobPrefix, StringComparison.Ordinal) && Guid.TryParse(job.Name.AsSpan(JobPrefix.Length), out var id) ? id : null;

    /// <summary>What an issue trigger is for, from its name; <see cref="JobKind.Other"/> for any other trigger.</summary>
    public static JobKind KindOf(TriggerKey trigger) =>
        Kinds.FirstOrDefault(k => trigger.Name.StartsWith(k.Prefix, StringComparison.Ordinal)) is { Prefix: not null } found ? found.Kind : JobKind.Other;

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
            .WithIdentity($"{DailyPrefix}{schedule.ProfileId}")
            .ForJob(job)
            .WithSchedule(CronScheduleBuilder
                .Create(CronFor(schedule))
                .InTimeZone(zone)

                // If Morning Ink was busy at the moment, run as soon as possible instead of skipping the day.
                .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
            .Build();
        await scheduler.UnscheduleJob(daily.Key, cancellationToken);
        await scheduler.ScheduleJob(daily, default, cancellationToken);

        // With a lead time, a second trigger builds the issue early; the daily one then only sends it.
        var buildKey = new TriggerKey($"{BuildPrefix}{schedule.ProfileId}");
        await scheduler.UnscheduleJob(buildKey, cancellationToken);
        if (schedule.LeadTime > TimeSpan.Zero)
        {
            var build = TriggerBuilder.Create(time)
                .WithIdentity(buildKey)
                .ForJob(job)
                .UsingJobData(LeadMinutesKey, (long)schedule.LeadTime.TotalMinutes)
                .WithSchedule(CronScheduleBuilder.Create(BuildCronFor(schedule)).InTimeZone(zone)
                    .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
                .Build();
            await scheduler.ScheduleJob(build, default, cancellationToken);
        }

        var now = time.GetUtcNow();
        var lastSlot = DeliverySlots.LastAtOrBefore(now, schedule.DeliveryTime, zone, schedule.Days);
        if (catchUp && now - lastSlot <= options.Value.CatchUpWindow)
        {
            // Harmless when the slot was already delivered: the publisher then does nothing.
            await scheduler.ScheduleJob(OneOff(job.Key, $"{CatchUpPrefix}{schedule.ProfileId}", lastSlot, now), ScheduleJobOptions.Replacing, cancellationToken);
        }
    }

    /// <summary>Builds and sends an issue right away, outside the schedule ("send now"), with the usual retries.</summary>
    /// <remarks>The slot is the current moment, so it is a new issue of its own; the seen-filter keeps it from repeating the last one.</remarks>
    public async Task SendNowAsync(IScheduler scheduler, ProfileSchedule schedule, CancellationToken cancellationToken)
    {
        if (!await scheduler.Exists(JobKeyFor(schedule.ProfileId), cancellationToken))
        {
            await ScheduleAsync(scheduler, schedule, cancellationToken, catchUp: false);
        }

        var now = time.GetUtcNow();
        await scheduler.ScheduleJob(OneOff(JobKeyFor(schedule.ProfileId), $"{NowPrefix}{schedule.ProfileId}-{now.ToUnixTimeMilliseconds()}", now, now), default, cancellationToken);
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
        await scheduler.ScheduleJob(OneOff(JobKeyFor(profileId), $"{RetryPrefix}{profileId}-{attempts}", slot, at), ScheduleJobOptions.Replacing, cancellationToken);
        LogRetry(logger, profileId, at);
    }

    /// <summary>Tries a delivery again shortly, because the early build of its issue still holds the profile.</summary>
    public async Task ScheduleAfterBuildAsync(IScheduler scheduler, Guid profileId, DateTimeOffset slot, CancellationToken cancellationToken) =>
        await scheduler.ScheduleJob(OneOff(JobKeyFor(profileId), $"{AfterBuildPrefix}{profileId}", slot, time.GetUtcNow() + BusyRetryDelay), ScheduleJobOptions.Replacing, cancellationToken);

    /// <summary>Quartz cron for the build ahead of delivery; across midnight, the build falls on the day before.</summary>
    internal static string BuildCronFor(ProfileSchedule schedule)
    {
        var start = schedule.DeliveryTime.ToTimeSpan() - schedule.LeadTime;
        var dayBefore = start < TimeSpan.Zero;
        var buildTime = TimeOnly.FromTimeSpan(dayBefore ? start + TimeSpan.FromDays(1) : start);
        var days = schedule.Days is { Count: > 0 } chosen && dayBefore
            ? chosen.Select(d => (DayOfWeek)(((int)d + 6) % 7)).ToList()
            : schedule.Days;
        return CronFor(schedule with { DeliveryTime = buildTime, Days = days });
    }

    /// <summary>Quartz cron for the delivery time: every day, or only on the chosen weekdays.</summary>
    internal static string CronFor(ProfileSchedule schedule)
    {
        var (minute, hour) = (schedule.DeliveryTime.Minute, schedule.DeliveryTime.Hour);
        if (DeliverySlots.IsEveryDay(schedule.Days))
        {
            return string.Create(CultureInfo.InvariantCulture, $"0 {minute} {hour} * * ?");
        }

        // Quartz wants "?" for the day of month when the day of week is given; its weekday names are SUN, MON, …
        var days = string.Join(',', schedule.Days.Distinct().Order().Select(d => d.ToString()[..3].ToUpperInvariant()));
        return string.Create(CultureInfo.InvariantCulture, $"0 {minute} {hour} ? * {days}");
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
