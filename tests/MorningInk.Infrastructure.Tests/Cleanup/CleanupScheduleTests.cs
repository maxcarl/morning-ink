using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Cleanup;
using MorningInk.Infrastructure.Tests.Persistence;
using Quartz;

namespace MorningInk.Infrastructure.Tests.Cleanup;

public sealed class CleanupScheduleTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task The_cleanup_runs_at_the_saved_time_in_the_saved_zone()
    {
        await using var services = await _host.StartAsync();
        await using var scope = services.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<CleanupSchedule>();
        Assert.Equal(new CleanupTime(new TimeOnly(3, 30), "Etc/UTC"), await schedule.GetAsync(CancellationToken.None)); // as before

        Assert.True(await schedule.SaveAsync(new CleanupTime(new TimeOnly(1, 15), "Europe/Berlin"), CancellationToken.None));
        Assert.False(await schedule.SaveAsync(new CleanupTime(new TimeOnly(1, 15), "Nowhere/Town"), CancellationToken.None));

        Assert.Equal(new CleanupTime(new TimeOnly(1, 15), "Europe/Berlin"), await schedule.GetAsync(CancellationToken.None));
        var trigger = Assert.IsType<ICronTrigger>(await CleanupTriggerAsync(services), exactMatch: false);
        Assert.Equal("0 15 1 * * ?", trigger.CronExpressionString);
        Assert.Equal("Europe/Berlin", trigger.TimeZone.Id);
    }

    [Fact]
    public async Task A_time_from_the_environment_wins_and_cannot_be_changed()
    {
        await using var services = await _host.StartAsync(new() { ["Retention:CleanupTime"] = "04:45" });
        await using var scope = services.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<CleanupSchedule>();

        Assert.True(schedule.IsLocked);
        Assert.False(await schedule.SaveAsync(new CleanupTime(new TimeOnly(1, 15), "Europe/Berlin"), CancellationToken.None));
        Assert.Equal(new CleanupTime(new TimeOnly(4, 45), "Etc/UTC"), await schedule.GetAsync(CancellationToken.None));
    }

    public void Dispose() => _host.Dispose();

    private static async Task<ITrigger?> CleanupTriggerAsync(IServiceProvider services)
    {
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        return await scheduler.GetTrigger(new TriggerKey("cleanup-daily"));
    }
}
