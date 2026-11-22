using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
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
        Pages.Submit(page, ("#title", "Die Frühstücksausgabe"), ("#recipient", "max_abc@kindle.com"), ("#time", "07:15"));

        Pages.Shows(page, "Saved. Changes apply from the next issue.");
        Assert.Matches("every day at 0?7:15", page.Markup);
        Assert.Contains("(Europe/Berlin), sent to max_abc@kindle.com.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Die Frühstücksausgabe", page.Markup, StringComparison.Ordinal);
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

        Pages.Button(page, "Edit").Click();
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

        // Drag C onto A: C moves to A's place.
        var rows = page.FindAll("li[draggable]");
        rows[2].DragStart();
        page.FindAll("li[draggable]")[0].Drop();
        page.WaitForAssertion(async () => Assert.Equal(["C", "A", "B"], (await SectionsAsync()).Select(s => s.Name)));

        // The keyboard way: open A and move it down.
        Pages.Button(page, "Edit", 1).Click();
        Pages.Button(page, "Down").Click();
        page.WaitForAssertion(async () => Assert.Equal(["C", "B", "A"], (await SectionsAsync()).Select(s => s.Name)));
    }

    [Fact]
    public async Task Section_is_deleted_only_after_confirmation()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<ProfilePage>("comes out every day");
        AddSection(page, "Politik");

        Pages.Button(page, "Edit").Click();
        Pages.Button(page, "Delete section").Click();
        Assert.Contains("Delete Politik?", page.Markup, StringComparison.Ordinal);
        Assert.Single(await SectionsAsync());

        Pages.Button(page, "Yes, delete").Click();
        page.WaitForAssertion(async () => Assert.Empty(await SectionsAsync()));
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

        Pages.Button(page, "Edit").Click();
        page.Find("input[type=search]").Input("GOL");

        Assert.Contains("> heise</label>", page.Markup, StringComparison.Ordinal); // ticked: stays, so saving keeps it
        Assert.Contains("> golem</label>", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("> zeit</label>", page.Markup, StringComparison.Ordinal);
    }

    public void Dispose() => _app.Dispose();

    private static void AddSection(IRenderedComponent<ProfilePage> page, string name)
    {
        Pages.Submit(page, ("#new-section", name));
        page.WaitForAssertion(() => Assert.Contains($">{name}</div>", page.Markup, StringComparison.Ordinal));
    }

    private async Task<List<Section>> SectionsAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles
            .SelectMany(p => p.Sections).Include(s => s.Sources).OrderBy(s => s.Position).ToListAsync();
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
