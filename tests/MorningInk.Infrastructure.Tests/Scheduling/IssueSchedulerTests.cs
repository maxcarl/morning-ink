using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Scheduling;
using MorningInk.Infrastructure.Tests.Delivery;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;
using Quartz;

namespace MorningInk.Infrastructure.Tests.Scheduling;

/// <summary>The test clock stands at 2026-11-21 7:00 in Berlin (6:00 UTC).</summary>
public sealed class IssueSchedulerTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly FakeSmtpServer _smtp = new();
    private readonly LocalServer _feed = new(async context =>
        await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Feeds", "edge-cases.xml"))));

    [Fact]
    public async Task Container_off_at_six_and_on_at_seven_still_delivers_the_issue()
    {
        await using var services = await StartAsync();
        await CreateProfileAsync(services, new TimeOnly(6, 0));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        await services.GetRequiredService<IssueScheduler>().ScheduleAllAsync(scheduler, CancellationToken.None);

        await WaitUntilAsync(() => _smtp.Messages.Count == 1);
        await scheduler.Shutdown(waitForJobsToComplete: true);
        Assert.Equal("Morning Ink 2026-11-21", Assert.Single(_smtp.Messages).Subject);
    }

    [Fact]
    public async Task Send_now_delivers_an_issue_outside_the_schedule()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(18, 0)); // nothing due: 18:00 was 13 hours ago
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        await services.GetRequiredService<IssueScheduler>().SendNowAsync(scheduler, new ProfileSchedule(profileId, new TimeOnly(18, 0), "Europe/Berlin"), CancellationToken.None);

        await WaitUntilAsync(() => _smtp.Messages.Count == 1);
        await scheduler.Shutdown(waitForJobsToComplete: true);
    }

    [Fact]
    public async Task Slot_older_than_the_catch_up_window_is_skipped()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(18, 0)); // last slot: yesterday 18:00, 13 hours ago
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await services.GetRequiredService<IssueScheduler>().ScheduleAllAsync(scheduler, CancellationToken.None);

        Assert.IsAssignableFrom<ICronTrigger>(Assert.Single(await scheduler.GetTriggersOfJob(IssueScheduler.JobKeyFor(profileId))));
    }

    [Fact]
    public async Task Daily_trigger_fires_at_the_profile_time_in_its_time_zone()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(6, 30));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await services.GetRequiredService<IssueScheduler>().ScheduleAllAsync(scheduler, CancellationToken.None);

        var daily = (await scheduler.GetTriggersOfJob(IssueScheduler.JobKeyFor(profileId))).OfType<ICronTrigger>().Single();
        Assert.Equal("Europe/Berlin", daily.TimeZone.Id);
        var next = TimeZoneInfo.ConvertTime(daily.NextFireTimeUtc!.Value, daily.TimeZone); // ! is safe: a daily trigger always fires again
        Assert.Equal((6, 30), (next.Hour, next.Minute));
    }

    [Fact]
    public async Task Weekend_issue_fires_only_on_saturday_and_sunday()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(6, 30), [DayOfWeek.Saturday, DayOfWeek.Sunday]);
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await services.GetRequiredService<IssueScheduler>().ScheduleAllAsync(scheduler, CancellationToken.None);

        // The test clock stands on Saturday 7:00, so the 6:30 slot of today is caught up and the next ones are Sunday and Saturday.
        var daily = (await scheduler.GetTriggersOfJob(IssueScheduler.JobKeyFor(profileId))).OfType<ICronTrigger>().Single();
        var first = daily.GetFireTimeAfter(DateTimeOffset.Parse("2026-11-21T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture))!.Value; // ! is safe: a weekly trigger always fires again
        var second = daily.GetFireTimeAfter(first)!.Value; // ! is safe: as above
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Saturday], new[] { first, second }.Select(t => TimeZoneInfo.ConvertTime(t, daily.TimeZone).DayOfWeek));
    }

    [Fact]
    public async Task Lead_time_builds_the_issue_before_its_delivery_time()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(6, 30), leadTime: TimeSpan.FromHours(2));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await services.GetRequiredService<IssueScheduler>().ScheduleAllAsync(scheduler, CancellationToken.None);

        var build = (ICronTrigger)(await scheduler.GetTrigger(new TriggerKey($"build-{profileId}")))!; // ! is safe: a lead time plans this trigger
        var next = TimeZoneInfo.ConvertTime(build.GetFireTimeAfter(DateTimeOffset.Parse("2026-11-21T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture))!.Value, build.TimeZone); // ! is safe: a daily trigger fires again
        Assert.Equal((4, 30), (next.Hour, next.Minute));
    }

    [Fact]
    public async Task Lead_time_across_midnight_builds_the_day_before()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(1, 0), [DayOfWeek.Saturday, DayOfWeek.Sunday], TimeSpan.FromHours(2));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await services.GetRequiredService<IssueScheduler>().ScheduleAllAsync(scheduler, CancellationToken.None);

        var build = (ICronTrigger)(await scheduler.GetTrigger(new TriggerKey($"build-{profileId}")))!; // ! is safe: a lead time plans this trigger
        var first = build.GetFireTimeAfter(DateTimeOffset.Parse("2026-11-21T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture))!.Value; // ! is safe: a weekly trigger fires again
        var second = build.GetFireTimeAfter(first)!.Value; // ! is safe: as above
        Assert.All(new[] { first, second }, t => Assert.Equal(23, TimeZoneInfo.ConvertTime(t, build.TimeZone).Hour));
        Assert.Equal([DayOfWeek.Saturday, DayOfWeek.Friday], new[] { first, second }.Select(t => TimeZoneInfo.ConvertTime(t, build.TimeZone).DayOfWeek));
    }

    [Fact]
    public async Task Jobs_overview_lists_planned_issues_with_their_edition_and_owner()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(18, 0), leadTime: TimeSpan.FromHours(1));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await services.GetRequiredService<IssueScheduler>().ScheduleAllAsync(scheduler, CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobsOverview>();
        var ownerId = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles.Where(p => p.Id == profileId).Select(p => p.UserId).SingleAsync();
        var view = await jobs.LoadAsync(ownerId, everyone: false, CancellationToken.None);

        Assert.Empty(view.Running);
        Assert.Equal("Europe/Berlin", view.TimeZoneId);
        Assert.Equal([JobKind.Build, JobKind.Daily], view.Planned.Select(j => j.Kind)); // the build an hour before the delivery
        Assert.All(view.Planned, j => Assert.Equal(("Morning Ink", "alex"), (j.Edition, j.Owner)));
        Assert.Empty((await jobs.LoadAsync(Guid.NewGuid(), everyone: false, CancellationToken.None)).Planned); // another user sees none of them
        Assert.Equal(2, (await jobs.LoadAsync(Guid.NewGuid(), everyone: true, CancellationToken.None)).Planned.Count(j => j.ProfileId == profileId));
    }

    [Fact]
    public async Task Rescheduling_does_not_add_a_second_daily_trigger()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(18, 0));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var issues = services.GetRequiredService<IssueScheduler>();

        await issues.ScheduleAllAsync(scheduler, CancellationToken.None);
        await issues.ScheduleAllAsync(scheduler, CancellationToken.None);

        Assert.Single(await scheduler.GetTriggersOfJob(IssueScheduler.JobKeyFor(profileId)));
    }

    [Fact]
    public async Task Failed_delivery_is_retried_with_growing_delay_until_the_limit()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services, new TimeOnly(18, 0));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var issues = services.GetRequiredService<IssueScheduler>();
        await issues.ScheduleAllAsync(scheduler, CancellationToken.None);
        var slot = TestHost.Now.AddHours(-1);

        await issues.ScheduleRetryAsync(scheduler, profileId, slot, attempts: 2, CancellationToken.None);
        await issues.ScheduleRetryAsync(scheduler, profileId, slot, attempts: 5, CancellationToken.None);

        var retry = await scheduler.GetTrigger(new TriggerKey($"retry-{profileId}-2"));
        Assert.Equal(TestHost.Now.AddMinutes(15), retry?.StartTimeUtc);
        Assert.Null(await scheduler.GetTrigger(new TriggerKey($"retry-{profileId}-5")));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 15)]
    [InlineData(3, 45)]
    [InlineData(4, 135)]
    public void Retry_delay_triples(int attempts, int minutes) =>
        Assert.Equal(TimeSpan.FromMinutes(minutes), IssueScheduler.RetryDelay(attempts, TimeSpan.FromMinutes(5)));

    public void Dispose()
    {
        _feed.Dispose();
        _smtp.Dispose();
        _host.Dispose();
    }

    // Like Web's AddApplication; Infrastructure tests cannot reference the Web project.
    private Task<ServiceProvider> StartAsync() => _host.StartAsync(
        new Dictionary<string, string?>
        {
            ["Http:AllowedHosts:0"] = $"127.0.0.1:{_feed.Port}",
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = _smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Smtp:Security"] = "None",
            ["Smtp:FromAddress"] = "morning-ink@example.com",
        },
        services => services.AddSingleton<ProfileLocks>().AddScoped<IssueBuilder>().AddScoped<IssueAiSteps>().AddScoped<IssueRelease>().AddScoped<IssuePublisher>());

    private async Task<Guid> CreateProfileAsync(IServiceProvider services, TimeOnly deliveryTime, List<DayOfWeek>? days = null, TimeSpan? leadTime = null)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "alex", Language = "de" };
        var source = new Source { UserId = user.Id, Name = "Test-Feed", Url = _feed.Url("/feed").ToString(), FetchFullText = false, MaxAge = TimeSpan.FromDays(3650) };
        var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = "name_abc@kindle.com", DeliveryTime = deliveryTime, Days = days ?? [], LeadTime = leadTime ?? TimeSpan.Zero };
        profile.Sections.Add(new Section { Name = "Nachrichten", Position = 1, Sources = { source } });
        db.AddRange(user, source, profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the scheduled issue.");
            await Task.Delay(100);
        }
    }
}
