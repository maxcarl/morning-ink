using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Scheduling;
using MorningInk.Infrastructure.Tests.Persistence;
using Quartz;

namespace MorningInk.Infrastructure.Tests.Profiles;

public sealed class ProfileEditorTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task First_visit_creates_a_profile_with_defaults()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");

        var profile = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));
        var again = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));

        Assert.Equal(profile.Id, again.Id);
        Assert.Equal(("Morning Ink", new TimeOnly(6, 0)), (profile.Title, profile.DeliveryTime));
    }

    [Fact]
    public async Task Saving_settings_reschedules_the_next_issue()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");
        var profile = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));

        var errors = await EditAsync(services, e => e.SaveSettingsAsync(user, new ProfileSettings("Die Frühstücksausgabe", "max_abc@kindle.com", "07:15", "Europe/Berlin", "52062"), CancellationToken.None));

        Assert.Empty(errors);
        var saved = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));
        Assert.Equal(("Die Frühstücksausgabe", "max_abc@kindle.com", new TimeOnly(7, 15), "52062"), (saved.Title, saved.Recipient, saved.DeliveryTime, saved.Region));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var daily = (await scheduler.GetTriggersOfJob(IssueScheduler.JobKeyFor(profile.Id))).OfType<ICronTrigger>().Single();
        var next = TimeZoneInfo.ConvertTime(daily.NextFireTimeUtc!.Value, daily.TimeZone); // ! is safe: a daily trigger always fires again
        Assert.Equal((7, 15), (next.Hour, next.Minute));
    }

    [Theory]
    [InlineData("", "max@kindle.com", "06:00", "Europe/Berlin", "Title")]
    [InlineData("Morning Ink", "not an address", "06:00", "Europe/Berlin", "Recipient")]
    [InlineData("Morning Ink", "a@b.de\r\nBcc: x@y.de", "06:00", "Europe/Berlin", "Recipient")]
    [InlineData("Morning Ink", "max@kindle.com", "25:00", "Europe/Berlin", "DeliveryTime")]
    [InlineData("Morning Ink", "max@kindle.com", "06:00", "Mars/Base", "TimeZone")]
    public async Task Invalid_settings_are_refused(string title, string recipient, string time, string zone, string field)
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");

        var errors = await EditAsync(services, e => e.SaveSettingsAsync(user, new ProfileSettings(title, recipient, time, zone, null), CancellationToken.None));

        Assert.Contains(field, errors);
    }

    [Fact]
    public async Task Sections_hold_the_users_sources_in_order()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");
        var feed = await CreateSourceAsync(services, user, "heise");

        await EditAsync(services, e => e.AddSectionAsync(user, "Technik", CancellationToken.None));
        await EditAsync(services, e => e.AddSectionAsync(user, "Politik", CancellationToken.None));
        var tech = (await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None))).Sections.Single(s => s.Name == "Technik");
        await EditAsync(services, e => e.SaveSectionAsync(user, tech.Id, "Tech", 5, [feed], CancellationToken.None));
        await EditAsync(services, e => e.MoveSectionAsync(user, tech.Id, up: false, CancellationToken.None));

        var profile = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));
        Assert.Equal(["Politik", "Tech"], profile.Sections.OrderBy(s => s.Position).Select(s => s.Name));
        var saved = profile.Sections.Single(s => s.Name == "Tech");
        Assert.Equal(5, saved.MaxArticles);
        Assert.Equal("heise", Assert.Single(saved.Sources).Name);
    }

    [Fact]
    public async Task Sources_and_sections_of_other_users_cannot_be_used()
    {
        await using var services = await _host.StartAsync();
        var alice = await CreateUserAsync(services, "alice");
        var bob = await CreateUserAsync(services, "bob");
        var bobsFeed = await CreateSourceAsync(services, bob, "bob's feed");
        await EditAsync(services, e => e.AddSectionAsync(alice, "News", CancellationToken.None));
        var section = (await EditAsync(services, e => e.GetOrCreateAsync(alice, CancellationToken.None))).Sections.Single();

        await EditAsync(services, e => e.SaveSectionAsync(alice, section.Id, "News", null, [bobsFeed], CancellationToken.None));
        var bobDeletes = await EditAsync(services, e => e.DeleteSectionAsync(bob, section.Id, CancellationToken.None));

        var profile = await EditAsync(services, e => e.GetOrCreateAsync(alice, CancellationToken.None));
        Assert.Empty(profile.Sections.Single().Sources);
        Assert.False(bobDeletes);
    }

    public void Dispose() => _host.Dispose();

    private static async Task<T> EditAsync<T>(IServiceProvider services, Func<ProfileEditor, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ProfileEditor>());
    }

    private static async Task<Guid> CreateUserAsync(IServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = name };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<Guid> CreateSourceAsync(IServiceProvider services, Guid user, string name)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var source = new Source { UserId = user, Name = name, Url = "https://example.com/feed" };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }
}
