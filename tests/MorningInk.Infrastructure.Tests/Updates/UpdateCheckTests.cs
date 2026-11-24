using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;
using MorningInk.Infrastructure.Updates;

namespace MorningInk.Infrastructure.Tests.Updates;

/// <summary>The update check against a local server that answers like GitHub's tag list.</summary>
public sealed class UpdateCheckTests : IDisposable
{
    private const string Tags = """[{"name":"v1.10.0"},{"name":"v1.2.0"},{"name":"v2.0.0-beta.1"},{"name":"nightly"},{"name":"v1.9.3"}]""";

    private readonly TestHost _host = new();
    private readonly ConcurrentQueue<string> _calls = new();
    private readonly LocalServer _server;

    public UpdateCheckTests() => _server = new LocalServer(async context =>
    {
        _calls.Enqueue(context.Request.Url!.PathAndQuery); // ! is safe: a received request always has a URL
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(Tags));
    });

    [Theory]
    [InlineData("1.2.0", "1.10.0")] // compared as numbers, not text; a pre-release is no update
    [InlineData("1.9.3-4-gabc1234", "1.10.0")] // a build from main after 1.9.3
    [InlineData("1.10.0", null)]
    [InlineData("1.10.0-2-gdef5678", null)] // ahead of the newest release
    public async Task A_newer_release_is_found_by_its_version_number(string running, string? expected)
    {
        await using var services = await StartAsync(running);

        Assert.Equal(expected, await CheckAsync(services));
    }

    [Theory]
    [InlineData("https://github.com/someone/morning-ink", "https://api.github.com/repos/someone/morning-ink/tags?per_page=100")]
    [InlineData("https://github.com/someone/morning-ink/", "https://api.github.com/repos/someone/morning-ink/tags?per_page=100")]
    [InlineData("https://git.example.com/someone/morning-ink", null)] // not GitHub: only with its own tag list
    public void The_tags_come_from_the_one_repository_address(string repository, string? tags)
    {
        Assert.Equal(tags, new RepositoryOptions { Url = repository }.Tags?.AbsoluteUri);
    }

    [Fact]
    public async Task GitHub_is_asked_at_most_once_a_day()
    {
        await using var services = await StartAsync("1.2.0");

        await CheckAsync(services);
        await CheckAsync(services);

        Assert.Single(_calls);
    }

    [Theory]
    [InlineData("dev", true)] // a local build has no release to compare with
    [InlineData("1.2.0", false)] // switched off
    public async Task Nothing_is_asked_without_a_release_or_when_switched_off(string running, bool check)
    {
        await using var services = await StartAsync(running, check);

        Assert.Null(await CheckAsync(services));
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task An_unreachable_github_means_no_notice()
    {
        await using var services = await _host.StartAsync(
            new() { ["Repository:TagsUrl"] = "http://127.0.0.1:1/tags" },
            s => s.AddSingleton(new AppVersion("1.2.0")));

        Assert.Null(await CheckAsync(services));
    }

    public void Dispose()
    {
        _server.Dispose();
        _host.Dispose();
    }

    private Task<ServiceProvider> StartAsync(string running, bool check = true) => _host.StartAsync(
        new()
        {
            ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}",
            ["Repository:TagsUrl"] = _server.Url("/tags").ToString(),
            ["Repository:CheckForUpdates"] = check.ToString(),
        },
        s => s.AddSingleton(new AppVersion(running)));

    private static async Task<string?> CheckAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UpdateCheck>().NewerReleaseAsync(CancellationToken.None);
    }
}
