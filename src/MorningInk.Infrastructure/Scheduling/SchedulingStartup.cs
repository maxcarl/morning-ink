using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MorningInk.Infrastructure.Cleanup;
using Quartz;

namespace MorningInk.Infrastructure.Scheduling;

/// <summary>Plans all profiles and the cleanup when the app starts; runs after migrations, which happen before the host starts.</summary>
internal sealed class SchedulingStartup(ISchedulerFactory factory, IssueScheduler issues, IOptions<RetentionOptions> retention, TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await factory.GetScheduler(cancellationToken);
        await issues.ScheduleAllAsync(scheduler, cancellationToken);
        await CleanupJob.ScheduleAsync(scheduler, retention, time, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
