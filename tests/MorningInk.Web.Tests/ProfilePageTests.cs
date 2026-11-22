using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Web.Tests;

public sealed class ProfilePageTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Settings_are_saved_and_shown()
    {
        var browser = await _app.SignedInAdminAsync();

        var response = await TestApp.SubmitAsync(browser, "/profile", new()
        {
            ["Title"] = "Die Frühstücksausgabe",
            ["Recipient"] = "max_abc@kindle.com",
            ["DeliveryTime"] = "07:15",
            ["TimeZoneId"] = "Europe/Berlin",
            ["Region"] = "52062",
        }, form: ("settings", null));
        var page = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()); // Blazor encodes umlauts and the narrow space before "AM"

        Assert.Contains("Saved. Changes apply from the next issue.", page, StringComparison.Ordinal);
        Assert.Contains("Every day at 7:15", page, StringComparison.Ordinal);
        Assert.Contains("(Europe/Berlin), sent to max_abc@kindle.com.", page, StringComparison.Ordinal);
        Assert.Contains("Die Frühstücksausgabe", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_recipient_is_explained_and_not_saved()
    {
        var browser = await _app.SignedInAdminAsync();

        var response = await TestApp.SubmitAsync(browser, "/profile", new()
        {
            ["Title"] = "Morning Ink",
            ["Recipient"] = "a@b.de, c@d.de",
            ["DeliveryTime"] = "06:00",
            ["TimeZoneId"] = "Europe/Berlin",
        }, form: ("settings", null));

        Assert.Contains("Please enter one email address", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Section_gets_its_sources_but_never_another_users()
    {
        var browser = await _app.SignedInAdminAsync();
        var (mine, foreign) = await CreateSourcesAsync();

        await TestApp.SubmitAsync(browser, "/profile", new() { ["NewSection"] = "Technik" }, form: ("add-section", null));
        var saved = await TestApp.SubmitAsync(browser, "/profile", [], form: ("section", null), sourceIds: [mine, foreign]);

        var page = await saved.Content.ReadAsStringAsync();
        Assert.Contains("Saved.", page, StringComparison.Ordinal);
        await using var scope = _app.Services.CreateAsyncScope();
        var section = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles
            .SelectMany(p => p.Sections).Include(s => s.Sources).SingleAsync();
        Assert.Equal([mine], section.Sources.Select(s => s.Id));
    }

    [Fact]
    public async Task Each_of_several_sections_can_be_saved_and_moved()
    {
        var browser = await _app.SignedInAdminAsync();
        var (mine, _) = await CreateSourcesAsync();
        await TestApp.SubmitAsync(browser, "/profile", new() { ["NewSection"] = "Politik" }, form: ("add-section", null));
        await TestApp.SubmitAsync(browser, "/profile", new() { ["NewSection"] = "Technik" }, form: ("add-section", null));
        var technik = await SectionIdAsync("Technik");

        var saved = await TestApp.SubmitAsync(browser, "/profile", new() { ["SectionAction"] = "save" }, form: ($"section-{technik}", null), sourceIds: [mine]);
        var moved = await TestApp.SubmitAsync(browser, "/profile", new() { ["SectionAction"] = "up" }, form: ($"section-{technik}", null));

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        await using var scope = _app.Services.CreateAsyncScope();
        var sections = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles
            .SelectMany(p => p.Sections).Include(s => s.Sources).OrderBy(s => s.Position).ToListAsync();
        Assert.Equal(["Technik", "Politik"], sections.Select(s => s.Name));
        Assert.Equal([mine], sections[0].Sources.Select(s => s.Id));
    }

    public void Dispose() => _app.Dispose();

    private async Task<Guid> SectionIdAsync(string name)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles
            .SelectMany(p => p.Sections).Where(s => s.Name == name).Select(s => s.Id).SingleAsync();
    }

    // One source of the admin, one of another user.
    private async Task<(Guid Mine, Guid Foreign)> CreateSourcesAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var admin = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
        var other = new User { Name = "erika" };
        var mine = new Source { UserId = admin.Id, Name = "heise", Url = "https://heise.de/rss" };
        var foreign = new Source { UserId = other.Id, Name = "fremd", Url = "https://example.com/rss" };
        db.AddRange(other, mine, foreign);
        await db.SaveChangesAsync();
        return (mine.Id, foreign.Id);
    }
}
