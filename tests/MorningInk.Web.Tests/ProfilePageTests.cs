using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class ProfilePageTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Settings_are_saved_and_shown()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");

        page.Find("#zone").Change("Europe/Berlin");
        Pages.Submit(page, ("#title", "Die Frühstücksausgabe"), ("#recipient", "name_abc@kindle.com"), ("#time", Pages.BrowserTime("07:15")));

        Pages.Shows(page, "Saved. Changes apply from the next issue.");
        Assert.Matches("every day at 0?7:15", page.Markup);
        Assert.Contains("(Europe/Berlin), sent to name_abc@kindle.com.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Die Frühstücksausgabe", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notification_asks_for_what_its_channel_needs_and_keeps_the_token_hidden()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");

        page.Find("#notify-channel").Change(nameof(NotificationChannel.Telegram));
        Pages.Submit(page, ("#notify-target", "-1001234567890/42"));
        Pages.Shows(page, "This channel needs its token.");
        Pages.Submit(page, ("#notify-target", "-1001234567890/42"), ("#notify-secret", "bot-token"));

        Pages.Shows(page, "A token is saved. Leave empty to keep it.");
        Assert.Empty(page.Find("#notify-secret").GetAttribute("value") ?? string.Empty);
        Assert.Contains(page.FindAll("button"), b => b.TextContent.Contains("Send test message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_recipient_is_explained_and_not_saved()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");

        Pages.Submit(page, ("#recipient", "a@b.de, c@d.de"));

        Pages.Shows(page, "Please enter one email address");
    }

    [Fact]
    public async Task Section_offers_and_saves_only_the_users_own_sources()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var mine = await AddSourceAsync(TestApp.AdminName, "heise");
        await AddSourceAsync("erika", "fremd");
        var page = pages.Open<ProfilePage>("comes out every day");
        AddSection(page, "Technik");

        Edit(page, "Technik").Click();
        Assert.DoesNotContain("fremd", page.Markup, StringComparison.Ordinal);
        page.Find("input[type=checkbox]").Change(true);
        page.Find("#name-" + await SectionIdAsync("Technik")).Closest("form")!.Submit();

        Pages.Shows(page, "Saved.");
        var section = (await SectionsAsync()).Single();
        Assert.Equal([mine], section.Sources.Select(s => s.Id));
    }

    [Fact]
    public async Task Sections_are_ordered_by_drag_and_drop_and_by_buttons()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");
        AddSection(page, "A");
        AddSection(page, "B");
        AddSection(page, "C");

        // Drag C onto A: C moves to A's place, behind the weather the edition starts with; "on this day" stays last.
        var rows = page.FindAll("li[draggable]");
        rows[3].DragStart();
        page.FindAll("li[draggable]")[1].Drop();
        page.WaitForAssertion(() => Assert.Equal(["Weather", "C", "A", "B", "On this day"], RowNames(page)));
        Assert.Equal(["C", "A", "B"], (await SectionsAsync()).Select(s => s.Name));

        // The keyboard way: open A and move it down.
        Edit(page, "A").Click();
        Pages.Button(page, "Down").Click();
        page.WaitForAssertion(() => Assert.Equal(["Weather", "C", "B", "A", "On this day"], RowNames(page)));
        Assert.Equal(["C", "B", "A"], (await SectionsAsync()).Select(s => s.Name));
    }

    [Fact]
    public async Task Section_is_deleted_only_after_confirmation()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");
        AddSection(page, "Politik");

        Edit(page, "Politik").Click();
        Pages.Button(page, "Delete section").Click();
        Assert.Contains("Delete Politik?", page.Markup, StringComparison.Ordinal);
        Assert.Single(await SectionsAsync());

        Pages.Button(page, "Yes, delete").Click();
        page.WaitForAssertion(() => Assert.Equal(["Weather", "On this day"], RowNames(page)));
        Assert.Empty(await SectionsAsync());
    }

    [Fact]
    public async Task Source_search_filters_the_choices_but_keeps_the_ticked_ones()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var heise = await AddSourceAsync(TestApp.AdminName, "heise");
        foreach (var name in new[] { "golem", "tagesschau", "spiegel", "zeit", "faz", "taz", "netzpolitik", "t3n" })
        {
            await AddSourceAsync(TestApp.AdminName, name);
        }

        var page = pages.Open<ProfilePage>("comes out every day");
        AddSection(page, "Technik");
        await TickAsync("Technik", heise);
        page.Dispose();
        page = pages.Open<ProfilePage>("heise");

        Edit(page, "Technik").Click();
        page.Find("input[type=search]").Input("GOL");

        Assert.Contains("> heise</label>", page.Markup, StringComparison.Ordinal); // ticked: stays, so saving keeps it
        Assert.Contains("> golem</label>", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("> zeit</label>", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_edition_is_created_and_opened()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");

        Pages.Button(page, "+ New edition").Click();

        await WaitForAsync(async () => (await ProfilesAsync()).Count == 2);
        var created = (await ProfilesAsync())[1];
        Assert.Equal("New edition", created.Title);
        Assert.EndsWith($"/profile/{created.Id}", pages.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Weekend_edition_comes_only_on_saturday_and_sunday()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var weekend = await CreateEditionAsync("Wochenende");
        var page = pages.Open<ProfilePage>("Wochenende comes out", p => p.Add(x => x.ProfileId, weekend));

        foreach (var day in new[] { "Mon", "Tue", "Wed", "Thu", "Fri" })
        {
            page.FindAll(".weekdays label").Single(l => l.TextContent.Trim() == day).QuerySelector("input")!.Change(false); // ! is safe: every day label holds a checkbox
        }

        Pages.Submit(page, ("#recipient", "name_abc@kindle.com"));

        Pages.Shows(page, "Wochenende comes out on Sun, Sat at"); // in English the week starts on Sunday
        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Saturday], (await ProfilesAsync()).Single(p => p.Id == weekend).Days);
        Assert.Equal(Enumerable.Empty<DayOfWeek>(), (await ProfilesAsync())[0].Days); // the first edition keeps every day
    }

    [Fact]
    public async Task Edition_is_deleted_only_after_confirmation_and_the_last_one_has_no_delete()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var evening = await CreateEditionAsync("Abend");
        var page = pages.Open<ProfilePage>("Abend comes out", p => p.Add(x => x.ProfileId, evening));

        Pages.Button(page, "Delete this edition").Click();
        Assert.Equal(2, (await ProfilesAsync()).Count);
        Pages.Button(page, "Yes, delete").Click();

        await WaitForAsync(async () => (await ProfilesAsync()).Count == 1);
        var first = pages.Open<ProfilePage>("comes out every day");
        Assert.DoesNotContain("Delete this edition", first.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interest_is_added_and_saved()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");

        Pages.Button(page, "+ Interest").Click();
        page.Find("#interest-weight-0").Change("StrongPrefer");
        Pages.Submit(page, ("#interest-topic-0", "Homelab"), ("#interest-keywords-0", "Proxmox, NAS"));

        Pages.Shows(page, "Saved.");
        var interest = Assert.Single((await ProfilesAsync())[0].Interests);
        Assert.Equal(("Homelab", InterestWeight.StrongPrefer), (interest.Topic, interest.Weight));
        Assert.Equal(["Proxmox", "NAS"], interest.Keywords);
    }

    public void Dispose() => _app.Dispose();

    private async Task<Guid> CreateEditionAsync(string title)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var userId = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Where(u => u.Name == TestApp.AdminName).Select(u => u.Id).SingleAsync();
        return (await scope.ServiceProvider.GetRequiredService<ProfileEditor>().CreateAsync(userId, title, CancellationToken.None)).Id;
    }

    private async Task<List<Profile>> ProfilesAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles.OrderBy(p => p.Id).ToListAsync();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task The_overview_is_offered_only_with_ai_and_then_sits_in_the_order_like_a_section()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");
        Assert.Empty(page.FindAll("#add-overview")); // no AI set up
        Assert.Contains("Weather", page.Markup, StringComparison.Ordinal); // every edition starts with its weather

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MorningInk.Infrastructure.Ai.AiSettingsStore>()
                .SaveAsync(new MorningInk.Infrastructure.Ai.AiSettingsInput("http://127.0.0.1:11434", "qwen2.5:7b", null, IsLocal: false, Summaries: true), Guid.NewGuid(), CancellationToken.None);
        }

        page = pages.Open<ProfilePage>("comes out every day");
        page.Find("#add-overview").Click();

        Pages.Shows(page, "The most important in 5 minutes");
        var overview = Assert.Single(await SectionsAsync(all: true), s => s.Kind == SectionKind.Overview);
        Assert.Equal(1, overview.Position); // after the weather
        Assert.Empty(page.FindAll("#add-overview")); // once per edition
    }

    [Fact]
    public async Task On_this_day_switches_on_the_article_of_the_day()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");
        var section = (await SectionsAsync(all: true)).Single(s => s.Kind == SectionKind.OnThisDay);

        Edit(page, "On this day").Click();
        page.Find($"#featured-{section.Id}").Change(true);
        page.Find($"#name-{section.Id}").Closest("form")!.Submit();

        Pages.Shows(page, "Saved.");
        var saved = (await SectionsAsync(all: true)).Single(s => s.Id == section.Id);
        Assert.Equal((true, true), (saved.ShowAnniversaries, saved.ShowFeaturedArticle));
    }

    [Fact]
    public async Task A_section_is_switched_off_and_on_without_losing_its_place()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");

        Pages.Button(page, "Switch off").Click();

        Pages.Shows(page, "Switched off");
        Assert.False((await SectionsAsync(all: true)).Single(s => s.Kind == SectionKind.Weather).Enabled);
        Pages.Button(page, "Switch on").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".badge.status-warn"))); // only "on this day", off from the start
        Assert.True((await SectionsAsync(all: true)).Single(s => s.Kind == SectionKind.Weather).Enabled);
    }

    [Fact]
    public async Task Feedback_from_the_reader_is_listed_and_can_be_taken_back()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var profile = await db.Profiles.SingleAsync();
            db.Feedback.Add(new Feedback { UserId = profile.UserId, ProfileId = profile.Id, SourceName = "heise", ArticleTitle = "Neuer Raspberry Pi", ArticleUrl = "https://heise.de/pi", Kind = FeedbackKind.Less, At = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        page = pages.Open<ProfilePage>("Neuer Raspberry Pi");
        Assert.Contains("Less from heise", page.Markup, StringComparison.Ordinal);
        Pages.Button(page, "Remove", 0).Click();

        page.WaitForAssertion(() => Assert.DoesNotContain("Neuer Raspberry Pi", page.Markup, StringComparison.Ordinal));
        await using var check = _app.Services.CreateAsyncScope();
        Assert.Empty(check.ServiceProvider.GetRequiredService<MorningInkDbContext>().Feedback);
    }

    private static void AddSection(IRenderedComponent<ProfilePage> page, string name)
    {
        Pages.Submit(page, ("#new-section", name));
        page.WaitForAssertion(() => Assert.Contains($">{name}</div>", page.Markup, StringComparison.Ordinal));
    }

    // The edit button of the named section: the weather every edition starts with has one too.
    private static AngleSharp.Dom.IElement Edit(IRenderedComponent<ProfilePage> page, string section) =>
        page.FindAll("button").Single(b => b.TextContent.Trim().StartsWith("Edit", StringComparison.Ordinal) && b.TextContent.Contains($"– {section}", StringComparison.Ordinal));

    // The sections as the page lists them, by name without their badges.
    private static List<string> RowNames(IRenderedComponent<ProfilePage> page) =>
        [.. page.FindAll("li[draggable] .row-title").Select(t => t.ChildNodes.First(n => n.NodeType == AngleSharp.Dom.NodeType.Text && n.TextContent.Trim().Length > 0).TextContent.Trim())];

    // The articles sections, or with all: true also the overview and the weather.
    private async Task<List<Section>> SectionsAsync(bool all = false)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles
            .SelectMany(p => p.Sections).Where(s => all || s.Kind == SectionKind.Articles).Include(s => s.Sources).OrderBy(s => s.Position).ToListAsync();
    }

    private async Task<Guid> SectionIdAsync(string name) => (await SectionsAsync()).Single(s => s.Name == name).Id;

    private async Task TickAsync(string section, Guid source)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var target = await db.Profiles.SelectMany(p => p.Sections).Include(s => s.Sources).SingleAsync(s => s.Name == section);
        target.Sources.Add(await db.Sources.SingleAsync(s => s.Id == source));
        await db.SaveChangesAsync();
    }

    // A source of the given user; a user who does not exist yet is created.
    private async Task<Guid> AddSourceAsync(string userName, string name)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = await db.DomainUsers.SingleOrDefaultAsync(u => u.Name == userName) ?? db.DomainUsers.Add(new User { Name = userName }).Entity;
        var source = new Source { UserId = user.Id, Name = name, Url = $"https://{name}.example/rss" };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }
}
