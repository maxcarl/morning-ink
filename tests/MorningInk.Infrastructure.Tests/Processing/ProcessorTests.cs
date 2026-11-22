using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Processing;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Processing;

public sealed class ProcessorTests
{
    private const string FeedText = "<p>Teaser from the feed.</p>";

    [Fact]
    public async Task Full_text_replaces_the_feed_text()
    {
        var page = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Pages", "netzpolitik-kids-act.html"));
        using var server = new LocalServer(async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(page);
        });

        var article = await RunAsync(server, Article(server.Url("/article")), new Source { UserId = Guid.NewGuid(), Name = "n", Url = "https://example.com" });

        Assert.Contains("Das und mehr geht aus der", article.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Feed_text_is_kept_when_the_page_cannot_be_fetched()
    {
        using var server = new LocalServer(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return Task.CompletedTask;
        });

        var article = await RunAsync(server, Article(server.Url("/gone")), new Source { UserId = Guid.NewGuid(), Name = "n", Url = "https://example.com" });

        Assert.Equal(FeedText, article.Content);
    }

    [Fact]
    public async Task Full_text_is_not_fetched_when_switched_off()
    {
        using var server = new LocalServer(context => context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("<html/>")).AsTask());

        var article = await RunAsync(server, Article(server.Url("/article")), new Source { UserId = Guid.NewGuid(), Name = "n", Url = "https://example.com", FetchFullText = false });

        Assert.Empty(server.Requests);
        Assert.Equal(FeedText, article.Content);
    }

    [Fact]
    public async Task Full_text_is_fetched_for_local_news_too()
    {
        // The news search links straight to the publisher, so local news get their article like any feed.
        using var server = new LocalServer(context => context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("<html/>")).AsTask());

        await RunAsync(server, Article(server.Url("/article")), new Source { UserId = Guid.NewGuid(), Name = "Lokales", Kind = SourceKind.Local, Place = "52062", FetchFullText = true });

        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Output_is_sanitized_whatever_the_source_delivers()
    {
        using var server = new LocalServer(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return Task.CompletedTask;
        });
        var dirty = Article(server.Url("/x")) with { Title = "<b>Hi</b> &amp; bye", Content = "<p onclick=\"x()\">Text<script>alert(1)</script></p>" };

        var article = await RunAsync(server, dirty, new Source { UserId = Guid.NewGuid(), Name = "n", Url = "https://example.com" });

        Assert.Equal("Hi & bye", article.Title);
        Assert.Equal("<p>Text</p>", article.Content);
    }

    private static FetchedArticle Article(Uri url) => new(null, "Title", url, null, null, "Teaser", FeedText);

    private static async Task<FetchedArticle> RunAsync(LocalServer server, FetchedArticle article, Source source)
    {
        using var services = BuildServices(server.Port);
        // Images and AI summaries have their own tests; here images would come from the internet and the AI needs a database.
        foreach (var processor in services.GetServices<IProcessor>().Where(p => p is not ImageProcessor and not MorningInk.Infrastructure.Ai.AiSummaryProcessor))
        {
            article = await processor.ProcessAsync(article, source, CancellationToken.None);
        }

        return article;
    }

    private static ServiceProvider BuildServices(int port) => new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = Path.GetTempPath(),
                ["Http:AllowedHosts:0"] = $"127.0.0.1:{port}",
            })
            .Build())
        .BuildServiceProvider();
}
