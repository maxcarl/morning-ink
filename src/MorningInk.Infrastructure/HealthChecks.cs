using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace MorningInk.Infrastructure;

/// <summary>Unhealthy when Quartz is not running: the web UI would still answer, but no issue would go out.</summary>
internal sealed class SchedulerHealthCheck(ISchedulerFactory schedulers) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        return await scheduler.GetStatus(cancellationToken) == SchedulerStatus.Running
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy();
    }
}

/// <summary>
/// Degraded, not unhealthy, when the volume runs low: Morning Ink still works until it is full, and Docker should not
/// mark the container broken for what a cleanup fixes. Degraded still answers 200; the body says "Degraded".
/// </summary>
internal sealed class FreeSpaceHealthCheck(DiskUsage disk) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(disk.LowOnSpace() ? HealthCheckResult.Degraded() : HealthCheckResult.Healthy());
}
