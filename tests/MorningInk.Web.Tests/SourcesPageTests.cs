using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Web.Tests;

public sealed class SourcesPageTests : IDisposable
{
    private readonly FeedServer _feeds = new();

    [Fact]
    public async Task Website_address_finds_the_feed_shows_a_preview_and_adds_it()
    {
        using var app = AllowingFeeds();
        var browser = await app.SignedInAdminAsync();

        var found = await TestApp.SubmitAsync(browser, "/sources", new() { ["Url"] = _feeds.SiteUrl }, form: ("discover", null));
        var page = await found.Content.ReadAsStringAsync();
        var added = await TestApp.SubmitAsync(browser, "/sources", page, new() { ["FeedUrl"] = _feeds.FeedUrl }, form: ("add", null));
        var list = await browser.GetStringAsync(TestApp.RedirectTarget(added));

        Assert.Contains(_feeds.FeedUrl, page, StringComparison.Ordinal);
        Assert.Contains("Erste Meldung", page, StringComparison.Ordinal); // preview before adding
        Assert.Equal("/sources?notice=added", TestApp.RedirectTarget(added));
        Assert.Contains("Source added.", list, StringComparison.Ordinal);
        Assert.Contains("Beispiel News", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saved_source_can_be_tested_and_deleted()
    {
        using var app = AllowingFeeds();
        var browser = await app.SignedInAdminAsync();
        var found = await TestApp.SubmitAsync(browser, "/sources", new() { ["Url"] = _feeds.FeedUrl }, form: ("discover", null));
        await TestApp.SubmitAsync(browser, "/sources", await found.Content.ReadAsStringAsync(), new() { ["FeedUrl"] = _feeds.FeedUrl }, form: ("add", null));

        var tested = await TestApp.SubmitAsync(browser, "/sources", new() { ["RowAction"] = "test" }, form: ("row", null));
        var deleted = await TestApp.SubmitAsync(browser, "/sources", new() { ["RowAction"] = "delete" }, form: ("row", null));
        var list = await browser.GetStringAsync("/sources");

        var testPage = await tested.Content.ReadAsStringAsync();
        Assert.Contains("works", testPage, StringComparison.Ordinal);
        Assert.Contains("Zweite Meldung", testPage, StringComparison.Ordinal);
        Assert.Equal("/sources?notice=deleted", TestApp.RedirectTarget(deleted));
        Assert.Contains("0 sources.", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_of_several_sources_can_be_tested_and_deleted()
    {
        using var app = AllowingFeeds();
        var browser = await app.SignedInAdminAsync();
        foreach (var name in new[] { "Erste", "Zweite" })
        {
            var found = await TestApp.SubmitAsync(browser, "/sources", new() { ["Url"] = _feeds.FeedUrl }, form: ("discover", null));
            await TestApp.SubmitAsync(browser, "/sources", await found.Content.ReadAsStringAsync(), new() { ["FeedUrl"] = _feeds.FeedUrl, ["Name"] = name }, form: ("add", null));
        }

        var ids = await SourceIdsAsync(app);
        var tested = await TestApp.SubmitAsync(browser, "/sources", new() { ["RowAction"] = "test" }, form: ($"row-{ids[1]}", null));
        var deleted = await TestApp.SubmitAsync(browser, "/sources", new() { ["RowAction"] = "delete" }, form: ($"row-{ids[0]}", null));

        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        Assert.Contains("row-selected", await tested.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("/sources?notice=deleted", TestApp.RedirectTarget(deleted));
        Assert.Equal([ids[1]], await SourceIdsAsync(app));
    }

    [Fact]
    public async Task Blocked_home_network_host_can_be_allowed_by_an_admin()
    {
        using var app = new TestApp(); // nothing allowed: the local feed server counts as home network
        var browser = await app.SignedInAdminAsync();
        var entry = $"127.0.0.1:{_feeds.Port}";

        var blocked = await TestApp.SubmitAsync(browser, "/sources", new() { ["Url"] = _feeds.SiteUrl }, form: ("discover", null));
        var blockedPage = await blocked.Content.ReadAsStringAsync();
        var allowed = await TestApp.SubmitAsync(browser, "/sources", blockedPage, [], form: ("allow", null));
        var allowedPage = await allowed.Content.ReadAsStringAsync();

        Assert.Contains($"{entry} is in your home network and blocked.", blockedPage, StringComparison.Ordinal);
        Assert.Contains($"Allow {entry}", blockedPage, StringComparison.Ordinal);
        Assert.Contains($"{entry} is allowed now.", allowedPage, StringComparison.Ordinal);
        Assert.Contains("Erste Meldung", allowedPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Users_who_are_not_admins_cannot_allow_hosts_even_with_a_forged_request()
    {
        using var app = new TestApp();
        await app.SignedInAdminAsync();
        var user = await app.SignedInUserAsync("erika");
        var entry = $"127.0.0.1:{_feeds.Port}";

        // The allow form is never shown to them, so the request is built from another form's antiforgery token.
        var forged = await TestApp.SubmitAsync(user, "/sources", new() { ["_handler"] = "allow", ["Host"] = entry, ["RetryUrl"] = _feeds.SiteUrl }, form: ("discover", null));

        // Refused before the handler (the form is not on their page), and the handler checks the role as well.
        Assert.DoesNotContain("is allowed now", await forged.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(app.Services.GetRequiredService<HostAllowlist>().IsAllowed("127.0.0.1", _feeds.Port));
    }

    [Fact]
    public async Task Invalid_address_is_explained()
    {
        using var app = AllowingFeeds();
        var browser = await app.SignedInAdminAsync();

        var response = await TestApp.SubmitAsync(browser, "/sources", new() { ["Url"] = "file:///etc/passwd" }, form: ("discover", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("could not be read", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    public void Dispose() => _feeds.Dispose();

    private static async Task<List<Guid>> SourceIdsAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInk.Infrastructure.Persistence.MorningInkDbContext>().Sources
            .OrderBy(s => s.Name).Select(s => s.Id).ToListAsync();
    }

    private TestApp AllowingFeeds() => new(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_feeds.Port}" });
}
