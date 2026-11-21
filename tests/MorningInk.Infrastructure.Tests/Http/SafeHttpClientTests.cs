using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Tests.Http;

public sealed class SafeHttpClientTests
{
    private static readonly AuthenticationHeaderValue Login = new("Basic", "bWF4OnNlY3JldA==");

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://192.168.178.1/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://localhost/")] // blocked after DNS resolution
    public async Task Internal_addresses_are_rejected(string url)
    {
        var result = await CreateClient().GetAsync(new Uri(url), cancellationToken: CancellationToken.None);

        Assert.Equal(FetchError.BlockedAddress, Assert.IsType<FetchResult.Failure>(result).Error);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/feed.xml")]
    [InlineData("http://user:password@example.com/")]
    [InlineData("/relative/feed.xml")]
    public async Task Urls_other_than_plain_http_and_https_are_rejected(string url)
    {
        var result = await CreateClient().GetAsync(new Uri(url, UriKind.RelativeOrAbsolute), cancellationToken: CancellationToken.None);

        Assert.Equal(FetchError.InvalidUrl, Assert.IsType<FetchResult.Failure>(result).Error);
    }

    [Fact]
    public async Task Ports_other_than_80_and_443_are_rejected()
    {
        var result = await CreateClient().GetAsync(new Uri("http://example.com:8080/"), cancellationToken: CancellationToken.None);

        Assert.Equal(FetchError.BlockedAddress, Assert.IsType<FetchResult.Failure>(result).Error);
    }

    [Fact]
    public async Task Redirect_into_the_home_network_is_rejected()
    {
        using var server = new LocalServer(context =>
        {
            context.Response.Redirect("http://192.168.178.1/admin");
            return Task.CompletedTask;
        });

        var result = await CreateClient(allow: $"127.0.0.1:{server.Port}").GetAsync(server.Url("/feed"), cancellationToken: CancellationToken.None);

        Assert.Equal(FetchError.BlockedAddress, Assert.IsType<FetchResult.Failure>(result).Error);
    }

    [Fact]
    public async Task Allowlisted_host_is_fetched()
    {
        using var server = new LocalServer(context => WriteAsync(context, "<rss/>"));

        var result = await CreateClient(allow: $"127.0.0.1:{server.Port}").GetAsync(server.Url("/feed"), cancellationToken: CancellationToken.None);

        var success = Assert.IsType<FetchResult.Success>(result);
        Assert.Equal("<rss/>", Encoding.UTF8.GetString(success.Content));
    }

    [Fact]
    public async Task More_than_five_redirects_are_rejected()
    {
        using var server = new LocalServer(context =>
        {
            context.Response.Redirect($"/hop{Random.Shared.Next()}");
            return Task.CompletedTask;
        });

        var result = await CreateClient(allow: $"127.0.0.1:{server.Port}").GetAsync(server.Url("/start"), cancellationToken: CancellationToken.None);

        Assert.Equal(FetchError.TooManyRedirects, Assert.IsType<FetchResult.Failure>(result).Error);
        Assert.Equal(6, server.Requests.Count); // the first request plus five redirects
    }

    [Fact]
    public async Task Responses_over_the_size_limit_are_rejected()
    {
        using var server = new LocalServer(context => WriteAsync(context, new string('x', 2048)));

        var result = await CreateClient(allow: $"127.0.0.1:{server.Port}", maxBytes: 1024).GetAsync(server.Url("/big"), cancellationToken: CancellationToken.None);

        Assert.Equal(FetchError.TooLarge, Assert.IsType<FetchResult.Failure>(result).Error);
    }

    [Fact]
    public async Task Slow_responses_time_out()
    {
        using var server = new LocalServer(context => Task.Delay(TimeSpan.FromSeconds(5)));

        var result = await CreateClient(allow: $"127.0.0.1:{server.Port}", timeout: "00:00:00.300").GetAsync(server.Url("/slow"), cancellationToken: CancellationToken.None);

        Assert.Equal(FetchError.Timeout, Assert.IsType<FetchResult.Failure>(result).Error);
    }

    [Fact]
    public async Task Credentials_are_dropped_when_a_redirect_changes_the_host()
    {
        using var server = new LocalServer(context =>
        {
            if (context.Request.Url!.AbsolutePath == "/feed") // ! is safe: a received request always has a URL
            {
                context.Response.Redirect($"http://localhost:{((IPEndPoint)context.Request.LocalEndPoint).Port}/moved");
                return Task.CompletedTask;
            }

            return WriteAsync(context, "<rss/>");
        });

        var result = await CreateClient(allow: $"127.0.0.1:{server.Port},localhost:{server.Port}")
            .GetAsync(server.Url("/feed"), Login, cancellationToken: CancellationToken.None);

        Assert.IsType<FetchResult.Success>(result);
        Assert.Equal(Login.ToString(), server.Requests[0].Authorization);
        Assert.Null(server.Requests[1].Authorization);
    }

    [Fact]
    public async Task Credentials_are_kept_on_a_redirect_within_the_same_host()
    {
        using var server = new LocalServer(context =>
        {
            if (context.Request.Url!.AbsolutePath == "/feed") // ! is safe: a received request always has a URL
            {
                context.Response.Redirect("/moved");
                return Task.CompletedTask;
            }

            return WriteAsync(context, "<rss/>");
        });

        await CreateClient(allow: $"127.0.0.1:{server.Port}").GetAsync(server.Url("/feed"), Login, cancellationToken: CancellationToken.None);

        Assert.Equal(Login.ToString(), server.Requests[1].Authorization);
    }

    private static async Task WriteAsync(HttpListenerContext context, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    private static SafeHttpClient CreateClient(string allow = "", int maxBytes = 1024 * 1024, string timeout = "00:00:10")
    {
        var settings = new Dictionary<string, string?>
        {
            ["Storage:DataDirectory"] = Path.GetTempPath(),
            ["Http:MaxResponseBytes"] = maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Http:Timeout"] = timeout,
        };
        var hosts = allow.Split(',', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < hosts.Length; i++)
        {
            settings[$"Http:AllowedHosts:{i}"] = hosts[i];
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider()
            .GetRequiredService<SafeHttpClient>();
    }
}
