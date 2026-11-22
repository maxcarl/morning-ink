using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Sources;

public sealed class FeedSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Articles_older_than_max_age_are_left_out_and_the_newest_come_first()
    {
        using var server = new LocalServer(context => WriteAsync(context, Feed(
            ("Two hours old", Now.AddHours(-2)),
            ("Two days old", Now.AddDays(-2)),
            ("One hour old", Now.AddHours(-1)))));

        var result = await FetchAsync(server, new Source { UserId = Guid.NewGuid(), Name = "test", Url = server.Url("/feed").ToString() });

        var success = Assert.IsType<SourceResult.Success>(result);
        Assert.Equal(["One hour old", "Two hours old"], success.Articles.Select(a => a.Title));
    }

    [Fact]
    public async Task At_most_max_articles_are_returned()
    {
        using var server = new LocalServer(context => WriteAsync(context, Feed(
            ("A", Now.AddMinutes(-1)), ("B", Now.AddMinutes(-2)), ("C", Now.AddMinutes(-3)))));

        var result = await FetchAsync(server, new Source { UserId = Guid.NewGuid(), Name = "test", Url = server.Url("/feed").ToString(), MaxArticles = 2 });

        Assert.Equal(["A", "B"], Assert.IsType<SourceResult.Success>(result).Articles.Select(a => a.Title));
    }

    [Fact]
    public async Task Unchanged_feed_is_not_downloaded_again()
    {
        using var server = new LocalServer(context =>
        {
            if (context.Request.Headers["If-None-Match"] == "\"v1\"")
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotModified;
                return Task.CompletedTask;
            }

            context.Response.Headers["ETag"] = "\"v1\"";
            return WriteAsync(context, Feed(("A", Now)));
        });
        var source = new Source { UserId = Guid.NewGuid(), Name = "test", Url = server.Url("/feed").ToString() };

        var first = Assert.IsType<SourceResult.Success>(await FetchAsync(server, source));
        source.ETag = first.ETag;
        var second = await FetchAsync(server, source);

        Assert.Equal("\"v1\"", first.ETag);
        Assert.IsType<SourceResult.NotModified>(second);
    }

    [Fact]
    public async Task Server_errors_become_a_failure()
    {
        using var server = new LocalServer(context =>
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return Task.CompletedTask;
        });

        var result = await FetchAsync(server, new Source { UserId = Guid.NewGuid(), Name = "test", Url = server.Url("/feed").ToString() });

        var failure = Assert.IsType<SourceResult.Failure>(result);
        Assert.Contains("500", failure.Message, StringComparison.Ordinal);
        Assert.Equal("HttpError", failure.Reason); // for the words in the user's language
        Assert.Equal(["127.0.0.1", "500"], failure.Arguments);
    }

    private static async Task<SourceResult> FetchAsync(LocalServer server, Source source)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = Path.GetTempPath(),
                ["Http:AllowedHosts:0"] = $"127.0.0.1:{server.Port}",
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        var feeds = services.GetServices<ISource>().Single(s => s.Kind == SourceKind.Rss);
        return await feeds.FetchAsync(source, CancellationToken.None);
    }

    private static string Feed(params (string Title, DateTimeOffset Published)[] items)
    {
        var xml = new StringBuilder("<rss version=\"2.0\"><channel><title>Test</title>");
        foreach (var (title, published) in items)
        {
            xml.Append(CultureInfo.InvariantCulture, $"<item><title>{title}</title><link>https://example.com/{Uri.EscapeDataString(title)}</link>")
                .Append(CultureInfo.InvariantCulture, $"<pubDate>{published.ToString("R", CultureInfo.InvariantCulture)}</pubDate></item>");
        }

        return xml.Append("</channel></rss>").ToString();
    }

    private static async Task WriteAsync(HttpListenerContext context, string body) =>
        await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
}
