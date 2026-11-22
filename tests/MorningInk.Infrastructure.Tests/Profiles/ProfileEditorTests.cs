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

        var errors = await EditAsync(services, async e => await e.SaveSettingsAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, new ProfileSettings("Die Frühstücksausgabe", "max_abc@kindle.com", "07:15", "Europe/Berlin", "52062", "de"), CancellationToken.None));

        Assert.Empty(errors);
        var saved = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));
        Assert.Equal(("Die Frühstücksausgabe", "max_abc@kindle.com", new TimeOnly(7, 15), "52062", "DE"), (saved.Title, saved.Recipient, saved.DeliveryTime, saved.Region, saved.Country));
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
    [InlineData("Morning Ink", "max@kindle.com", "06:00", "Europe/Berlin", "Country", "XX")]
    public async Task Invalid_settings_are_refused(string title, string recipient, string time, string zone, string field, string? country = null)
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");

        var errors = await EditAsync(services, async e => await e.SaveSettingsAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, new ProfileSettings(title, recipient, time, zone, null, country), CancellationToken.None));

        Assert.Contains(field, errors);
    }

    [Fact]
    public async Task Sections_hold_the_users_sources_in_order()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");
        var feed = await CreateSourceAsync(services, user, "heise");

        await EditAsync(services, async e => await e.AddSectionAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, "Technik", CancellationToken.None));
        await EditAsync(services, async e => await e.AddSectionAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, "Politik", CancellationToken.None));
        var tech = (await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None))).Sections.Single(s => s.Name == "Technik");
        await EditAsync(services, e => e.SaveSectionAsync(user, tech.Id, "Tech", 5, [feed], CancellationToken.None));
        await EditAsync(services, e => e.MoveSectionAsync(user, tech.Id, 1, CancellationToken.None));

        var profile = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));
        Assert.Equal(["Politik", "Tech"], profile.Sections.OrderBy(s => s.Position).Select(s => s.Name));
        var saved = profile.Sections.Single(s => s.Name == "Tech");
        Assert.Equal(5, saved.MaxArticles);
        Assert.Equal("heise", Assert.Single(saved.Sources).Name);
    }

    [Theory]
    [InlineData("C", 0, new[] { "C", "A", "B" })] // dragged to the top
    [InlineData("A", 2, new[] { "B", "C", "A" })] // dragged to the bottom
    [InlineData("A", 9, new[] { "B", "C", "A" })] // beyond the end: last
    [InlineData("B", -1, new[] { "B", "A", "C" })] // before the start: first
    public async Task Section_moves_to_any_position(string name, int index, string[] expected)
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");
        foreach (var section in new[] { "A", "B", "C" })
        {
            await EditAsync(services, async e => await e.AddSectionAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, section, CancellationToken.None));
        }

        var id = (await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None))).Sections.Single(s => s.Name == name).Id;
        await EditAsync(services, e => e.MoveSectionAsync(user, id, index, CancellationToken.None));

        var profile = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));
        Assert.Equal(expected, profile.Sections.OrderBy(s => s.Position).Select(s => s.Name));
    }

    [Fact]
    public async Task Second_edition_takes_over_the_first_ones_delivery_and_has_its_own_schedule()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");
        await EditAsync(services, async e => await e.SaveSettingsAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, new ProfileSettings("Morgen", "max_abc@kindle.com", "06:00", "Europe/Vienna", "1010", "AT"), CancellationToken.None));

        var evening = await EditAsync(services, e => e.CreateAsync(user, "Abend", CancellationToken.None));

        var all = await EditAsync(services, e => e.ListAsync(user, CancellationToken.None));
        Assert.Equal(["Morgen", "Abend"], all.Select(p => p.Title));
        Assert.Equal(("max_abc@kindle.com", "Europe/Vienna", "AT"), (evening.Recipient, evening.TimeZoneId, evening.Country));
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        Assert.True(await scheduler.Exists(IssueScheduler.JobKeyFor(evening.Id)));
    }

    [Fact]
    public async Task Deleting_an_edition_removes_its_issues_files_and_schedule_but_never_the_last_one()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");
        var first = await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None));
        var second = await EditAsync(services, e => e.CreateAsync(user, "Abend", CancellationToken.None));
        var file = await AddIssueWithFileAsync(services, second.Id);

        var deleted = await EditAsync(services, e => e.DeleteAsync(user, second.Id, CancellationToken.None));
        var lastOne = await EditAsync(services, e => e.DeleteAsync(user, first.Id, CancellationToken.None));

        Assert.True(deleted);
        Assert.False(lastOne);
        Assert.False(File.Exists(file));
        await using var scope = services.CreateAsyncScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Issues);
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        Assert.False(await scheduler.Exists(IssueScheduler.JobKeyFor(second.Id)));
    }

    [Theory]
    [InlineData(new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }, new[] { DayOfWeek.Sunday, DayOfWeek.Saturday })]
    [InlineData(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }, new DayOfWeek[0])] // all seven: every day
    public async Task Weekdays_are_saved(DayOfWeek[] chosen, DayOfWeek[] stored)
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");

        var errors = await EditAsync(services, async e => await e.SaveSettingsAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, new ProfileSettings("Morning Ink", "max_abc@kindle.com", "06:00", "Europe/Berlin", null, null, chosen), CancellationToken.None));

        Assert.Empty(errors);
        Assert.Equal(stored, (await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None))).Days);
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(0, true)]
    [InlineData(13 * 60, false)] // more than half a day ahead is no lead time but a mistake
    [InlineData(-30, false)]
    public async Task Lead_time_is_saved_within_half_a_day(int minutes, bool valid)
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");

        var errors = await EditAsync(services, async e => await e.SaveSettingsAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, new ProfileSettings("Morning Ink", "max_abc@kindle.com", "06:00", "Europe/Berlin", null, null, null, TimeSpan.FromMinutes(minutes)), CancellationToken.None));

        Assert.Equal(valid, errors.Count == 0);
        Assert.Equal(valid ? TimeSpan.FromMinutes(minutes) : TimeSpan.Zero, (await EditAsync(services, e => e.GetOrCreateAsync(user, CancellationToken.None))).LeadTime);
    }

    [Fact]
    public async Task No_weekday_at_all_is_refused()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services, "max");

        var errors = await EditAsync(services, async e => await e.SaveSettingsAsync(user, (await e.GetOrCreateAsync(user, CancellationToken.None)).Id, new ProfileSettings("Morning Ink", "max_abc@kindle.com", "06:00", "Europe/Berlin", null, null, []), CancellationToken.None));

        Assert.Contains("Days", errors);
    }

    [Fact]
    public async Task Another_users_edition_cannot_be_changed_or_deleted()
    {
        await using var services = await _host.StartAsync();
        var alice = await CreateUserAsync(services, "alice");
        var bob = await CreateUserAsync(services, "bob");
        var alicesSecond = await EditAsync(services, e => e.CreateAsync(alice, "Abend", CancellationToken.None));

        var saved = await EditAsync(services, e => e.SaveSettingsAsync(bob, alicesSecond.Id, new ProfileSettings("Bob", "bob@kindle.com", "06:00", "Europe/Berlin", null), CancellationToken.None));
        var added = await EditAsync(services, e => e.AddSectionAsync(bob, alicesSecond.Id, "Bob", CancellationToken.None));
        var deleted = await EditAsync(services, e => e.DeleteAsync(bob, alicesSecond.Id, CancellationToken.None));

        Assert.Contains("Profile", saved);
        Assert.False(added);
        Assert.False(deleted);
        Assert.Equal("Abend", (await EditAsync(services, e => e.FindAsync(alice, alicesSecond.Id, CancellationToken.None)))?.Title);
    }

    [Fact]
    public async Task Sources_and_sections_of_other_users_cannot_be_used()
    {
        await using var services = await _host.StartAsync();
        var alice = await CreateUserAsync(services, "alice");
        var bob = await CreateUserAsync(services, "bob");
        var bobsFeed = await CreateSourceAsync(services, bob, "bob's feed");
        await EditAsync(services, async e => await e.AddSectionAsync(alice, (await e.GetOrCreateAsync(alice, CancellationToken.None)).Id, "News", CancellationToken.None));
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

    // An issue with a file of its own, as the publisher leaves it behind.
    private static async Task<string> AddIssueWithFileAsync(IServiceProvider services, Guid profileId)
    {
        await using var scope = services.CreateAsyncScope();
        var issue = new Issue { ProfileId = profileId, Number = 1, ScheduledFor = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow };
        issue.FileName = $"{issue.Id}.epub";
        var path = scope.ServiceProvider.GetRequiredService<MorningInk.Application.Publishing.IIssueFiles>().PathFor(issue.FileName);
        await File.WriteAllTextAsync(path, "epub");
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return path;
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
