using Microsoft.Extensions.Hosting;
using Quartz;

namespace MorningInk.Infrastructure.Scheduling;

/// <summary>Plans all profiles when the app starts; runs after migrations, which happen before the host starts.</summary>
internal sealed class SchedulingStartup(ISchedulerFactory factory, IssueScheduler issues) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) =>
        await issues.ScheduleAllAsync(await factory.GetScheduler(cancellationToken), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
