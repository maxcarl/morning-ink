using Microsoft.Extensions.Logging;
using MorningInk.Application.Publishing;
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

        // Catch-up and retry triggers carry their slot; the daily trigger's slot is its scheduled time.
        var slot = data.ContainsKey(IssueScheduler.SlotKey)
            ? DateTimeOffset.FromUnixTimeMilliseconds(data.GetLong(IssueScheduler.SlotKey))
            : context.ScheduledFireTimeUtc ?? context.FireTimeUtc;

        var result = await publisher.PublishAsync(profileId, slot, cancellationToken);
        LogResult(logger, profileId, slot, result.GetType().Name);

        if (result is PublishResult.DeliveryFailed { Retryable: true } failed)
        {
            await scheduler.ScheduleRetryAsync(context.Scheduler, profileId, slot, failed.Attempts, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Issue of profile {ProfileId} for {Slot}: {Result}")]
    private static partial void LogResult(ILogger logger, Guid profileId, DateTimeOffset slot, string result);
}
