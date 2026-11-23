using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>Finding feeds, managing sources per user and allowing home-network hosts.</summary>
public sealed class SourceCatalogTests : IDisposable
{
    private static readonly byte[] Feed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Feeds", "edge-cases.xml"));

    private readonly TestHost _host = new();
    private readonly LocalServer _site = new(async context =>
    {
        var path = context.Request.Url!.AbsolutePath; // ! is safe: a received request always has a URL
        byte[]? body = path switch
        {
            "/" => "<html><head><title>Beispiel</title><link rel=\"alternate\" type=\"application/rss+xml\" title=\"Beispiel News\" href=\"/news.xml\"><link rel=\"alternate\" type=\"text/html\" href=\"/other\"></head></html>"u8.ToArray(),
            "/news.xml" => Feed,
            "/blog" => "<html><head><title>Blog</title></head><body>Kein Link</body></html>"u8.ToArray(),
            "/feed" => Feed,
            _ => null,
        };
        if (body is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        await context.Response.OutputStream.WriteAsync(body);
    });

    [Fact]
    public async Task Website_url_finds_its_feed()
    {
        await using var services = await StartAsync(allow: true);

        var result = await Discovery(services).DiscoverAsync(_site.Url("/").ToString(), CancellationToken.None);

        var feed = Assert.Single(Assert.IsType<FeedDiscoveryResult.Found>(result).Feeds);
        Assert.Equal(_site.Url("/news.xml"), feed.Url);
        Assert.Equal("Beispiel News", feed.Title);
    }

    [Fact]
    public async Task Feed_url_is_taken_as_it_is()
    {
        await using var services = await StartAsync(allow: true);

        var result = await Discovery(services).DiscoverAsync(_site.Url("/news.xml").ToString(), CancellationToken.None);

        var feed = Assert.Single(Assert.IsType<FeedDiscoveryResult.Found>(result).Feeds);
        Assert.Equal(_site.Url("/news.xml"), feed.Url);
        Assert.Equal("Edge cases", feed.Title); // the feed's own title, not the host
    }

    [Fact]
    public async Task Page_without_feed_link_tries_the_usual_paths()
    {
        await using var services = await StartAsync(allow: true);

        var result = await Discovery(services).DiscoverAsync(_site.Url("/blog").ToString(), CancellationToken.None);

        Assert.Equal(_site.Url("/feed"), Assert.Single(Assert.IsType<FeedDiscoveryResult.Found>(result).Feeds).Url);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public async Task Invalid_input_finds_nothing(string input)
    {
        await using var services = await StartAsync(allow: true);

        Assert.IsType<FeedDiscoveryResult.Failure>(await Discovery(services).DiscoverAsync(input, CancellationToken.None));
    }

    [Fact]
    public async Task Blocked_host_names_the_entry_an_admin_could_allow_and_works_once_allowed()
    {
        await using var services = await StartAsync(allow: false);
        var discovery = Discovery(services);

        var blocked = Assert.IsType<FeedDiscoveryResult.Failure>(await discovery.DiscoverAsync(_site.Url("/").ToString(), CancellationToken.None));
        await services.GetRequiredService<HostAllowlist>().AddAsync(blocked.BlockedHost!, Guid.NewGuid(), CancellationToken.None); // ! is safe: asserted by the type check below
        var allowed = await discovery.DiscoverAsync(_site.Url("/").ToString(), CancellationToken.None);

        Assert.Equal($"127.0.0.1:{_site.Port}", blocked.BlockedHost);
        Assert.IsType<FeedDiscoveryResult.Found>(allowed);
    }

    [Fact]
    public async Task Allowed_hosts_survive_a_restart()
    {
        await using (var services = await StartAsync(allow: false))
        {
            await services.GetRequiredService<HostAllowlist>().AddAsync($"127.0.0.1:{_site.Port}", Guid.NewGuid(), CancellationToken.None);
        }

        await using var restarted = await StartAsync(allow: false);
        await restarted.GetRequiredService<HostAllowlist>().LoadAsync(CancellationToken.None);

        Assert.True(restarted.GetRequiredService<HostAllowlist>().IsAllowed("127.0.0.1", _site.Port));
    }

    [Fact]
    public async Task Sources_belong_to_their_user()
    {
        await using var services = await StartAsync(allow: true);
        var (alice, bob) = (await CreateUserAsync(services, "alice"), await CreateUserAsync(services, "bob"));
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SourceCatalog>();

        var added = Assert.IsType<SourceSaveResult.Saved>(await catalog.AddFeedAsync(alice, "Beispiel", _site.Url("/news.xml").ToString(), fetchFullText: false, CancellationToken.None));

        Assert.Single(await catalog.ListAsync(alice, CancellationToken.None));
        Assert.Empty(await catalog.ListAsync(bob, CancellationToken.None));
        Assert.False(await catalog.DeleteAsync(bob, added.Source.Id, CancellationToken.None));
        Assert.Null(await catalog.TestAsync(bob, added.Source.Id, CancellationToken.None));
        Assert.True(await catalog.DeleteAsync(alice, added.Source.Id, CancellationToken.None));
        Assert.Empty(await catalog.ListAsync(alice, CancellationToken.None));
    }

    [Fact]
    public async Task Testing_a_source_shows_its_articles_without_touching_its_cache()
    {
        await using var services = await StartAsync(allow: true);
        var user = await CreateUserAsync(services, "alex");
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SourceCatalog>();
        var added = Assert.IsType<SourceSaveResult.Saved>(await catalog.AddFeedAsync(user, "Beispiel", _site.Url("/news.xml").ToString(), fetchFullText: false, CancellationToken.None));

        var result = await catalog.TestAsync(user, added.Source.Id, CancellationToken.None);

        Assert.Equal(3, Assert.IsType<SourceResult.Success>(result).Articles.Count);
        Assert.Null(added.Source.ETag);
    }

    [Theory]
    [InlineData("", "https://example.com/feed")]
    [InlineData("Name", "file:///etc/passwd")]
    [InlineData("Name", "not a url")]
    public async Task Invalid_sources_are_not_saved(string name, string url)
    {
        await using var services = await StartAsync(allow: true);
        var user = await CreateUserAsync(services, "alex");
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SourceCatalog>();

        Assert.IsType<SourceSaveResult.Invalid>(await catalog.AddFeedAsync(user, name, url, fetchFullText: true, CancellationToken.None));
        Assert.Empty(await catalog.ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Local_source_keeps_its_place()
    {
        await using var services = await StartAsync(allow: true);
        var user = await CreateUserAsync(services, "alex");
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SourceCatalog>();

        await catalog.AddLocalAsync(user, "Lokales Aachen", "52062", "de", CancellationToken.None);
        var invalid = await catalog.AddLocalAsync(user, "Lokales Mars", "1", "XX", CancellationToken.None);

        var source = Assert.Single(await catalog.ListAsync(user, CancellationToken.None));
        Assert.Equal((SourceKind.Local, "52062", "DE", true), (source.Kind, source.Place, source.Country, source.FetchFullText)); // full text from the publisher
        Assert.IsType<SourceSaveResult.Invalid>(invalid);
    }

    [Fact]
    public async Task Source_settings_and_filters_are_saved_for_the_owner_only()
    {
        await using var services = await StartAsync(allow: true);
        var max = await CreateUserAsync(services, "alex");
        var erika = await CreateUserAsync(services, "erika");
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SourceCatalog>();
        var added = Assert.IsType<SourceSaveResult.Saved>(await catalog.AddLocalAsync(max, "Lokales", "80331", "DE", CancellationToken.None)).Source;
        var settings = new SourceSettings("Lokales München", FetchFullText: true, MaxArticles: 8, SourceSettings.ParseKeywords("Polizei, Feuerwehr\nStadtrat"), SourceSettings.ParseKeywords("FC Bayern; Oktoberfest"), MinWords: 80, SkipLiveTickers: true, SkipAds: false, MaxWords: 2000);

        var foreign = await catalog.SaveSettingsAsync(erika, added.Id, settings, CancellationToken.None);
        var saved = await catalog.SaveSettingsAsync(max, added.Id, settings, CancellationToken.None);

        Assert.IsType<SourceSaveResult.Invalid>(foreign);
        var source = Assert.IsType<SourceSaveResult.Saved>(saved).Source;
        Assert.Equal(("Lokales München", 8, 80, 2000, true), (source.Name, source.MaxArticles, source.MinWords, source.MaxWords, source.SkipLiveTickers));
        Assert.False(source.SkipAds);
        Assert.Equal(["Polizei", "Feuerwehr", "Stadtrat"], source.IncludeKeywords);
        Assert.Equal(["FC Bayern", "Oktoberfest"], source.ExcludeKeywords);
    }

    [Theory]
    [InlineData("", 10, null, "NameRequired")]
    [InlineData("Feed", 0, null, "MaxArticlesInvalid")]
    [InlineData("Feed", 10, 0, "MinWordsInvalid")]
    [InlineData("Feed", 10, null, "MaxWordsInvalid", 0)]
    [InlineData("Feed", 10, 500, "MaxWordsInvalid", 400)] // below the minimum, nothing could ever fit
    public async Task Invalid_source_settings_are_refused(string name, int maxArticles, int? minWords, string reason, int? maxWords = null)
    {
        await using var services = await StartAsync(allow: true);
        var max = await CreateUserAsync(services, "alex");
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SourceCatalog>();
        var added = Assert.IsType<SourceSaveResult.Saved>(await catalog.AddLocalAsync(max, "Lokales", "80331", "DE", CancellationToken.None)).Source;

        var result = await catalog.SaveSettingsAsync(max, added.Id, new SourceSettings(name, true, maxArticles, [], [], minWords, false, MaxWords: maxWords), CancellationToken.None);

        Assert.Equal(reason, Assert.IsType<SourceSaveResult.Invalid>(result).Reason);
    }

    public void Dispose()
    {
        _site.Dispose();
        _host.Dispose();
    }

    private static FeedDiscovery Discovery(IServiceProvider services) => services.GetRequiredService<FeedDiscovery>();

    private Task<ServiceProvider> StartAsync(bool allow) =>
        _host.StartAsync(allow ? new Dictionary<string, string?> { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_site.Port}" } : null);

    private static async Task<Guid> CreateUserAsync(IServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = name };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
