using Quartz;

namespace MorningInk.Infrastructure.Cleanup;

/// <summary>Runs the cleanup once a day; Quartz creates it in its own DI scope per run.</summary>
[DisallowConcurrentExecution]
internal sealed class CleanupJob(CleanupService cleanup) : IJob
{
    public static readonly JobKey Key = new("cleanup");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) =>
        await cleanup.RunAsync(cancellationToken);

    /// <summary>Plans the daily run at the local time in the zone, replacing an earlier plan; see <see cref="CleanupSchedule"/>.</summary>
    public static async Task ScheduleAsync(IScheduler scheduler, TimeOnly at, TimeZoneInfo zone, TimeProvider time, CancellationToken cancellationToken)
    {
        var job = JobBuilder.Create<CleanupJob>().WithIdentity(Key).StoreDurably().Build();
        var trigger = TriggerBuilder.Create(time)
            .WithIdentity("cleanup-daily")
            .ForJob(job)
            .WithSchedule(CronScheduleBuilder
                .Create(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"0 {at.Minute} {at.Hour} * * ?"))
                .InTimeZone(zone))
            .Build();
        await scheduler.ScheduleJob(job, trigger, ScheduleJobOptions.Replacing, cancellationToken);
    }
}
