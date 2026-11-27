using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MorningInk.Infrastructure.Cleanup;
using Quartz;

namespace MorningInk.Infrastructure.Scheduling;

/// <summary>Plans all profiles and the cleanup when the app starts; runs after migrations, which happen before the host starts.</summary>
internal sealed class SchedulingStartup(ISchedulerFactory factory, IssueScheduler issues, IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scheduler = await factory.GetScheduler(cancellationToken);
        await issues.ScheduleAllAsync(scheduler, cancellationToken);
        await using var scope = scopes.CreateAsyncScope(); // the cleanup time is in the database
        await scope.ServiceProvider.GetRequiredService<CleanupSchedule>().ScheduleAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
