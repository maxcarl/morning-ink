using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>Hacker News and Reddit: what the community voted up, from a minimum score on, without the noise.</summary>
public sealed class CommunitySourceTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly LocalServer _server = new(async context =>
    {
        var file = context.Request.Url!.AbsolutePath switch // ! is safe: a received request always has a URL
        {
            "/hn/search_by_date" => "hn-search.json",
            "/r/selfhosted/top.json" => "reddit-top.json",
            _ => null,
        };
        if (file is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Community", file)));
    });

    [Fact]
    public async Task Hacker_news_asks_for_stories_from_the_minimum_score_and_links_to_the_article()
    {
        await using var services = await StartAsync();
        var source = await AddAsync(services, (catalog, user) => catalog.AddHackerNewsAsync(user, 150, CancellationToken.None));

        var articles = Assert.IsType<SourceResult.Success>(await FetchAsync(services, source)).Articles;

        Assert.Equal(["A new approach to garbage collection", "Ask HN: What are you working on?"], articles.Select(a => a.Title)); // no "javascript:" link
        Assert.Equal(new Uri("https://blog.example.com/gc"), articles[0].Url);
        Assert.Equal("412 points, 128 comments", articles[0].Author);
        Assert.False(articles[0].IsComplete); // the publisher's page has the text
        var ask = articles[1];
        Assert.Equal(new Uri("https://news.ycombinator.com/item?id=41000002"), ask.Url);
        Assert.True(ask.IsComplete);
        Assert.Contains("Share what you are building", ask.Content, StringComparison.Ordinal);
        var search = Uri.UnescapeDataString(Assert.Single(_server.Requests).Url);
        Assert.Contains("numericFilters=points>=150,created_at_i>", search, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reddit_keeps_what_reached_the_minimum_and_leaves_out_pinned_video_and_nsfw_posts()
    {
        await using var services = await StartAsync();
        var source = await AddAsync(services, (catalog, user) => catalog.AddRedditAsync(user, "r/selfhosted", 100, CancellationToken.None));

        var articles = Assert.IsType<SourceResult.Success>(await FetchAsync(services, source)).Articles;

        Assert.Equal(["Self-hosting a newspaper for my e-reader", "New release of a media server"], articles.Select(a => a.Title));
        var post = articles[0];
        Assert.True(post.IsComplete);
        Assert.Contains("<strong>small</strong>", post.Content, StringComparison.Ordinal); // decoded once, cleaned later like every article
        Assert.Equal(new Uri("https://www.reddit.com/r/selfhosted/comments/abc1/selfhosting/"), post.Url);
        Assert.Equal(new Uri("https://github.com/example/media/releases"), articles[1].Url);
        Assert.Contains("/r/selfhosted/top.json?t=day", Assert.Single(_server.Requests).Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("selfhosted")]
    [InlineData("r/selfhosted")]
    [InlineData("/r/selfhosted/")]
    [InlineData("https://www.reddit.com/r/selfhosted/")]
    public async Task A_subreddit_is_taken_however_it_is_written(string written)
    {
        await using var services = await StartAsync();

        var source = await AddAsync(services, (catalog, user) => catalog.AddRedditAsync(user, written, 100, CancellationToken.None));

        Assert.Equal(("r/selfhosted", "https://www.reddit.com/r/selfhosted"), (source.Name, source.Url));
    }

    [Theory]
    [InlineData("../admin")]
    [InlineData("a")]
    [InlineData("self hosted")]
    public async Task A_subreddit_name_that_is_none_is_refused(string written)
    {
        await using var services = await StartAsync();
        await using var scope = services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<SourceCatalog>().AddRedditAsync(Guid.NewGuid(), written, 100, CancellationToken.None);

        Assert.Equal("SubredditInvalid", Assert.IsType<SourceSaveResult.Invalid>(result).Reason);
    }

    [Fact]
    public async Task The_full_text_step_leaves_a_complete_post_as_it_is()
    {
        await using var services = await StartAsync();
        await using var scope = services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetServices<IProcessor>().Single(p => p.GetType().Name == "FullTextProcessor");
        var post = new FetchedArticle("t3_x", "Post", new Uri($"http://127.0.0.1:{_server.Port}/r/selfhosted/comments/x/"), null, null, null, "<p>The post itself</p>") { IsComplete = true };

        var processed = await processor.ProcessAsync(post, new Source { UserId = Guid.NewGuid(), Name = "r/selfhosted", Kind = SourceKind.Reddit }, CancellationToken.None);

        Assert.Equal("<p>The post itself</p>", processed.Content);
        Assert.Empty(_server.Requests); // not even asked
    }

    public void Dispose()
    {
        _server.Dispose();
        _host.Dispose();
    }

    private Task<ServiceProvider> StartAsync() => _host.StartAsync(new()
    {
        ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}",
        ["Community:HackerNewsApi"] = _server.Url("/hn").ToString(),
        ["Community:RedditUrl"] = _server.Url("/").ToString(),
    });

    private static async Task<Source> AddAsync(IServiceProvider services, Func<SourceCatalog, Guid, Task<SourceSaveResult>> add)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = $"reader-{Guid.NewGuid():N}" };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return Assert.IsType<SourceSaveResult.Saved>(await add(scope.ServiceProvider.GetRequiredService<SourceCatalog>(), user.Id)).Source;
    }

    private static async Task<SourceResult> FetchAsync(IServiceProvider services, Source source)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ISource>().Single(s => s.Kind == source.Kind).FetchAsync(source, CancellationToken.None);
    }
}
