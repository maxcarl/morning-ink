using Microsoft.Extensions.Logging;
using MorningInk.Application.Publishing;
using MorningInk.Application.Scheduling;
using Quartz;

namespace MorningInk.Infrastructure.Scheduling;

/// <summary>Quartz job that publishes one profile's issue; Quartz creates it in its own DI scope per run.</summary>
[DisallowConcurrentExecution]
internal sealed partial class PublishIssueJob(IssuePublisher publisher, IssueScheduler scheduler, ILogger<PublishIssueJob> logger) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var data = context.MergedJobDataMap;
        var profileId = Guid.Parse(data.GetString(IssueScheduler.ProfileIdKey)!); // ! is safe: every job is built with the profile id

        // Catch-up and retry triggers carry their slot; the daily trigger's slot is its scheduled time, and the build
        // trigger's slot lies its lead time after its own.
        var fired = context.ScheduledFireTimeUtc ?? context.FireTimeUtc;
        var prepareOnly = data.ContainsKey(IssueScheduler.LeadMinutesKey);
        var slot = data.ContainsKey(IssueScheduler.SlotKey) ? DateTimeOffset.FromUnixTimeMilliseconds(data.GetLong(IssueScheduler.SlotKey))
            : prepareOnly ? fired + TimeSpan.FromMinutes(data.GetLong(IssueScheduler.LeadMinutesKey))
            : fired;

        // Planned before the run, so a failing build does not end the chain of early builds. The trigger carries the
        // expression in Quartz's own form, as the scheduler wrote it.
        if (prepareOnly && CronExpression.TryParse(data.GetString(IssueScheduler.CronKey), out var parsed))
        {
            var cron = parsed.WithTimeZone(ProfileTimeZone.Find(data.GetString(IssueScheduler.ZoneKey) ?? string.Empty));
            await scheduler.PlanCronBuildAsync(context.Scheduler, profileId, cron, TimeSpan.FromMinutes(data.GetLong(IssueScheduler.LeadMinutesKey)), cancellationToken);
        }

        var result = await publisher.PublishAsync(profileId, slot, cancellationToken, prepareOnly, new StepLog(logger, profileId));
        LogResult(logger, profileId, slot, result.GetType().Name);

        // The early build still runs at delivery time: send as soon as it is done instead of skipping the day.
        if (result is PublishResult.AlreadyRunning && !prepareOnly)
        {
            await scheduler.ScheduleAfterBuildAsync(context.Scheduler, profileId, slot, cancellationToken);
        }

        if (result is PublishResult.DeliveryFailed { Retryable: true } failed)
        {
            await scheduler.ScheduleRetryAsync(context.Scheduler, profileId, slot, failed.Attempts, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Issue of profile {ProfileId}: {Stage}")]
    private static partial void LogStage(ILogger logger, Guid profileId, IssueStage stage);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Issue of profile {ProfileId}: {Stage} {Done}/{Total}")]
    private static partial void LogCount(ILogger logger, Guid profileId, IssueStage stage, int done, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Issue of profile {ProfileId} for {Slot}: {Result}")]
    private static partial void LogResult(ILogger logger, Guid profileId, DateTimeOffset slot, string result);

    // Each new stage at Information, so the container log shows where a long run is; the counts within at Debug.
    private sealed class StepLog(ILogger logger, Guid profileId) : IProgress<IssueStep>
    {
        private IssueStage? _stage;

        public void Report(IssueStep value)
        {
            if (value.Stage != _stage)
            {
                _stage = value.Stage;
                LogStage(logger, profileId, value.Stage);
            }

            if (value.Total > 0)
            {
                LogCount(logger, profileId, value.Stage, value.Done, value.Total);
            }
        }
    }
}
