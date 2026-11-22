using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class SourcesPageTests : IDisposable
{
    private readonly FeedServer _feeds = new();

    [Fact]
    public async Task Website_address_finds_the_feed_shows_a_preview_and_adds_it()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Sources>("0 feeds");

        Pages.Submit(page, ("#url", _feeds.SiteUrl));
        Pages.Shows(page, "Erste Meldung"); // preview before adding
        Assert.Contains(_feeds.FeedUrl, page.Markup, StringComparison.Ordinal);
        Pages.Submit(page, ("#feed-name", "Beispiel News"));

        Pages.Shows(page, "Source added.");
        Assert.Contains("Beispiel News", page.Markup, StringComparison.Ordinal);
        Assert.Equal(["Beispiel News"], (await SourcesAsync(app)).Select(s => s.Name));
    }

    [Fact]
    public async Task Each_source_can_be_tested_and_is_deleted_only_after_confirmation()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        await AddSourcesAsync(app, "Erste", "Zweite");
        var page = pages.Open<Sources>("2 feeds");

        Pages.Button(page, "Test", 1).Click();
        Pages.Shows(page, "Zweite Meldung");
        Assert.Contains("works", page.Markup, StringComparison.Ordinal);
        Assert.Contains("row-selected", page.Markup, StringComparison.Ordinal);

        Pages.Button(page, "Delete", 0).Click();
        Assert.Contains("Delete Erste?", page.Markup, StringComparison.Ordinal);
        Assert.Equal(2, (await SourcesAsync(app)).Count); // nothing deleted before the confirmation
        Pages.Button(page, "Yes, delete").Click();

        Pages.Shows(page, "Source deleted.");
        Assert.Equal(["Zweite"], (await SourcesAsync(app)).Select(s => s.Name));
    }

    [Fact]
    public async Task Source_is_renamed_and_gets_filters()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        await AddSourcesAsync(app, "Erste");
        var page = pages.Open<Sources>("1 feeds");

        Pages.Button(page, "Edit").Click();
        page.Find("#source-live").Change(true);
        Pages.Submit(page, ("#source-name", "Lokalnachrichten"), ("#source-exclude", "Fußball, Anzeige"), ("#source-minwords", "80"));

        Pages.Shows(page, "Lokalnachrichten");
        var source = Assert.Single(await SourcesAsync(app));
        Assert.Equal(("Lokalnachrichten", 80, true), (source.Name, source.MinWords, source.SkipLiveTickers));
        Assert.Equal(["Fußball", "Anzeige"], source.ExcludeKeywords);
        Assert.Contains("filtered", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Blocked_home_network_host_can_be_allowed_by_an_admin()
    {
        using var app = new TestApp(); // nothing allowed: the local feed server counts as home network
        using var pages = await Pages.AsAdminAsync(app);
        var entry = $"127.0.0.1:{_feeds.Port}";
        var page = pages.Open<Sources>("0 feeds");

        Pages.Submit(page, ("#url", _feeds.SiteUrl));
        Pages.Shows(page, $"{entry} is in your home network and blocked.");
        Pages.Button(page, $"Allow {entry}").Click();

        Pages.Shows(page, $"{entry} is allowed now.");
        Pages.Shows(page, "Erste Meldung"); // and the search runs again
    }

    [Fact]
    public async Task Users_who_are_not_admins_are_not_offered_to_allow_hosts()
    {
        using var app = new TestApp();
        await app.SignedInAdminAsync();
        using var pages = await Pages.AsUserAsync(app, "erika");
        var page = pages.Open<Sources>("0 feeds");

        Pages.Submit(page, ("#url", _feeds.SiteUrl));

        Pages.Shows(page, "Ask an admin to allow it.");
        Assert.DoesNotContain("Allow 127.0.0.1", page.Markup, StringComparison.Ordinal);
        Assert.False(app.Services.GetRequiredService<HostAllowlist>().IsAllowed("127.0.0.1", _feeds.Port));
    }

    [Fact]
    public async Task Invalid_address_is_explained()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Sources>("0 feeds");

        Pages.Submit(page, ("#url", "file:///etc/passwd"));

        Pages.Shows(page, "could not be read");
    }

    public void Dispose() => _feeds.Dispose();

    private static async Task<List<Source>> SourcesAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Sources.OrderBy(s => s.Name).ToListAsync();
    }

    private async Task AddSourcesAsync(TestApp app, params string[] names)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var admin = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
        db.Sources.AddRange(names.Select(name => new Source { UserId = admin.Id, Name = name, Url = _feeds.FeedUrl }));
        await db.SaveChangesAsync();
    }

    private TestApp AllowingFeeds() => new(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_feeds.Port}" });
}
