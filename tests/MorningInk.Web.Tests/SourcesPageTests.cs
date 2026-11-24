using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
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
    public async Task Test_applies_the_filters_and_says_why_an_article_is_left_out()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        await AddSourcesAsync(app, "Erste");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            (await db.Sources.SingleAsync()).ExcludeKeywords = ["Zweite"];
            await db.SaveChangesAsync();
        }

        var page = pages.Open<Sources>("1 feeds");
        Pages.Button(page, "Test").Click();

        Pages.Shows(page, "Left out by the filters: 1 of 2");
        var kept = page.Find(".headline-list:not(.headline-list-dropped)");
        var dropped = page.Find(".headline-list-dropped");
        Assert.Contains("Erste Meldung", kept.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Zweite Meldung", kept.TextContent, StringComparison.Ordinal);
        Assert.Contains("Zweite Meldung", dropped.TextContent, StringComparison.Ordinal);
        Assert.Contains("Contains “Zweite”", dropped.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FreshRss_that_refuses_the_sign_in_is_explained()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Sources>("0 feeds");

        // The feed server is no FreshRSS: its answer to the sign-in carries no session.
        Pages.Submit(page, ("#freshrss-url", _feeds.SiteUrl), ("#freshrss-user", "reader"), ("#freshrss-password", "api-password"));

        Pages.Shows(page, "FreshRSS could not be read:");
        Assert.Empty(await SourcesAsync(app));
    }

    [Fact]
    public async Task Karakeep_that_refuses_the_key_is_explained()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Sources>("0 feeds");

        // The feed server is no Karakeep: it has no API to accept the key.
        Pages.Submit(page, ("#karakeep-url", _feeds.SiteUrl), ("#karakeep-key", "ak1_key"));

        Pages.Shows(page, "Karakeep could not be read:");
        Assert.Empty(await SourcesAsync(app));
    }

    [Fact]
    public async Task Karakeep_is_listed_as_read_later_and_can_keep_its_bookmarks()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var admin = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
            db.Sources.Add(new Source { UserId = admin.Id, Name = "Karakeep", Kind = SourceKind.Karakeep, Url = "https://karakeep.example.com/" });
            await db.SaveChangesAsync();
        }

        var page = pages.Open<Sources>("Karakeep");
        Assert.Contains(page.FindAll("h2.department"), h => h.TextContent == "Read later");
        Pages.Button(page, "Edit").Click();
        Assert.Contains("Archive bookmarks in Karakeep", page.Markup, StringComparison.Ordinal);
        page.Find("#source-mark-read").Change(false);
        Pages.Submit(page, ("#source-name", "Karakeep"));

        Pages.Shows(page, "Karakeep");
        Assert.False(Assert.Single(await SourcesAsync(app)).MarkReadAfterDelivery);
    }

    [Fact]
    public async Task FreshRss_sources_are_listed_on_their_own_and_can_keep_articles_unread()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var admin = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
            db.Sources.Add(new Source { UserId = admin.Id, Name = "Technik", Kind = SourceKind.FreshRss, Url = "https://freshrss.example.com/api/greader.php", Stream = "user/-/label/Technik" });
            await db.SaveChangesAsync();
        }

        var page = pages.Open<Sources>("Technik");
        Assert.Contains(page.FindAll("h2.department"), h => h.TextContent == "FreshRSS");
        Pages.Button(page, "Edit").Click();
        page.Find("#source-mark-read").Change(false);
        Pages.Submit(page, ("#source-name", "Technik"));

        Pages.Shows(page, "Technik");
        Assert.False(Assert.Single(await SourcesAsync(app)).MarkReadAfterDelivery);
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
        page.Find("#source-ads").Change(false);
        Pages.Submit(page, ("#source-name", "Lokalnachrichten"), ("#source-exclude", "Fußball, Anzeige"), ("#source-minwords", "80"));

        Pages.Shows(page, "Lokalnachrichten");
        var source = Assert.Single(await SourcesAsync(app));
        Assert.Equal(("Lokalnachrichten", 80, true), (source.Name, source.MinWords, source.SkipLiveTickers));
        Assert.Equal(["Fußball", "Anzeige"], source.ExcludeKeywords);
        Assert.False(source.SkipAds);
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

    [Fact]
    public async Task Newsletter_folder_is_added_after_a_check_and_its_images_can_be_switched_off()
    {
        var mail = new MimeKit.MimeMessage { Subject = "Wochenbrief", Date = DateTimeOffset.UtcNow.AddHours(-1) };
        mail.From.Add(new MimeKit.MailboxAddress("Brief", "brief@example.com"));
        mail.Body = new MimeKit.TextPart("plain") { Text = "Hallo" };
        using var imap = new MorningInk.Infrastructure.Tests.Sources.FakeImapServer("reader", "imap-password", new() { ["Newsletter"] = [mail] });
        using var app = new TestApp(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{imap.Port}" });
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Sources>("0 feeds");

        Pages.Submit(page, ("#newsletter-server", $"127.0.0.1:{imap.Port}"), ("#newsletter-user", "reader"), ("#newsletter-password", "wrong"), ("#newsletter-folder", "Newsletter"), ("#newsletter-name", "Wochenbrief"));
        Pages.Shows(page, "refused the sign-in");
        Pages.Submit(page, ("#newsletter-server", $"127.0.0.1:{imap.Port}"), ("#newsletter-user", "reader"), ("#newsletter-password", "imap-password"), ("#newsletter-folder", "Newsletter"), ("#newsletter-name", "Wochenbrief"));
        Pages.Shows(page, "Source added.");

        Assert.Contains(page.FindAll("h2.department"), h => h.TextContent == "Newsletters");
        Pages.Button(page, "Edit").Click();
        page.Find("#source-images").Change(false);
        Pages.Submit(page, ("#source-sender", "example.com"));
        Pages.Shows(page, "from example.com");
        var source = Assert.Single(await SourcesAsync(app));
        Assert.Equal((SourceKind.Newsletter, "Newsletter", false), (source.Kind, source.Stream, source.LoadImages));
    }

    [Fact]
    public async Task Hacker_news_and_a_subreddit_are_added_with_their_minimum_score()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Sources>("0 feeds");

        Pages.Submit(page, ("#hn-min", "200"));
        Pages.Shows(page, "Source added.");
        Pages.Submit(page, ("#reddit-name", "r/selfhosted"), ("#reddit-min", "50"));
        Pages.Shows(page, "r/selfhosted");

        var sources = await SourcesAsync(app);
        Assert.Equal((SourceKind.HackerNews, 200), (sources.Single(s => s.Name == "Hacker News").Kind, sources.Single(s => s.Name == "Hacker News").MinScore));
        Assert.Equal((SourceKind.Reddit, 50), (sources.Single(s => s.Name == "r/selfhosted").Kind, sources.Single(s => s.Name == "r/selfhosted").MinScore));
        Assert.Contains("from 200 points", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opml_file_from_another_reader_adds_its_feeds()
    {
        using var app = AllowingFeeds();
        using var pages = await Pages.AsAdminAsync(app);
        await AddSourcesAsync(app, "Schon da");
        var page = pages.Open<Sources>("1 feed");
        var opml = $"""
            <opml version="2.0"><body><outline text="Technik">
              <outline text="Beispiel Blog" type="rss" xmlUrl="https://blog.example.com/feed.xml"/>
              <outline text="Schon da" type="rss" xmlUrl="{_feeds.FeedUrl}"/>
            </outline></body></opml>
            """;

        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(opml, "subscriptions.opml"));

        Pages.Shows(page, "1 feed(s) added, 1 already there.");
        Pages.Shows(page, "Beispiel Blog");
    }

    [Fact]
    public async Task Export_has_the_own_feeds_as_opml_and_needs_a_sign_in()
    {
        using var app = AllowingFeeds();
        var browser = await app.SignedInAdminAsync();
        await AddSourcesAsync(app, "Beispiel News");

        var response = await browser.GetAsync("/sources/export.opml");
        var anonymous = await app.Browser().GetAsync("/sources/export.opml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("Beispiel News", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
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
