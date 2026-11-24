using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>Karakeep through its REST API, played by a local server with answers in Karakeep's format.</summary>
public sealed class KarakeepSourceTests : IDisposable
{
    private const string ApiKey = "ak1_test_key";

    private readonly TestHost _host = new();
    private readonly ConcurrentQueue<(string Method, string Path, string? Authorization, string Body)> _calls = new();
    private readonly LocalServer _server;

    public KarakeepSourceTests() => _server = new LocalServer(async context =>
    {
        var request = context.Request;
        var body = await new StreamReader(request.InputStream).ReadToEndAsync();
        var url = request.Url!; // ! is safe: a received request always has a URL
        _calls.Enqueue((request.HttpMethod, url.PathAndQuery, request.Headers["Authorization"], body));
        var authorized = request.Headers["Authorization"] == $"Bearer {ApiKey}";
        var (status, answer) = (url.AbsolutePath, request.HttpMethod) switch
        {
            _ when !authorized => (401, """{"code":"UNAUTHORIZED"}"""),
            ("/api/v1/users/me", "GET") => (200, """{"id":"user-1","name":"Reader"}"""),
            ("/api/v1/bookmarks", "GET") => (200, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Karakeep", "bookmarks.json"))),
            ("/api/v1/bookmarks/bm-saved-article", "PATCH") => (200, """{"id":"bm-saved-article","archived":true}"""),
            ("/moved/api/v1/users/me", _) => (302, string.Empty),
            _ => (404, "{}"),
        };
        context.Response.StatusCode = status;
        if (status == 302)
        {
            context.Response.RedirectLocation = $"http://localhost:{url.Port}/elsewhere";
        }

        await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(answer));
    });

    [Fact]
    public async Task Unarchived_bookmarks_come_with_their_saved_text_and_web_links_only()
    {
        await using var services = await StartAsync();
        var (source, _) = await AddAsync(services, ApiKey);

        var result = Assert.IsType<SourceResult.Success>(await FetchAsync(services, source!));

        Assert.Equal(["My own title", "A new bridge in Sampletown"], result.Articles.Select(a => a.Title)); // newest first, no note, no script link
        var saved = result.Articles.Single(a => a.Id == "bm-saved-article");
        Assert.Equal(("A. Writer", "<p>The town of Sampletown opened its new bridge on Friday.</p>", true), (saved.Author, saved.Content, saved.IsComplete));
        Assert.Equal("Only a description, no full text yet.", result.Articles.Single(a => a.Id == "bm-own-title").Summary);
        Assert.Contains(_calls, c => c.Path.StartsWith("/api/v1/bookmarks?", StringComparison.Ordinal) && c.Path.Contains("archived=false", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_wrong_key_is_refused_when_adding_and_nothing_is_saved()
    {
        await using var services = await StartAsync();

        var (source, failure) = await AddAsync(services, "wrong");

        Assert.Null(source);
        Assert.Equal("KarakeepLogin", failure?.Reason);
        await using var scope = services.CreateAsyncScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Credentials);
    }

    [Fact]
    public async Task Delivered_bookmarks_are_archived_and_can_be_left_alone()
    {
        await using var services = await StartAsync();
        var (source, _) = await AddAsync(services, ApiKey);

        var archived = await AcknowledgeAsync(services, source!, ["bm-saved-article"]);
        source!.MarkReadAfterDelivery = false;
        var left = await AcknowledgeAsync(services, source, ["bm-saved-article"]);

        Assert.Equal((1, null), (archived.Marked, archived.Problem));
        Assert.Equal(0, left.Marked);
        var patch = Assert.Single(_calls, c => c.Method == "PATCH");
        Assert.Contains("\"archived\":true", patch.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_key_is_never_sent_on_through_a_redirect()
    {
        await using var services = await StartAsync();

        await using var scope = services.CreateAsyncScope();
        var (_, failure) = await scope.ServiceProvider.GetRequiredService<ConnectedSources>().AddKarakeepAsync(Guid.NewGuid(), _server.Url("/moved").ToString(), ApiKey, CancellationToken.None);

        Assert.NotNull(failure);
        Assert.DoesNotContain(_calls, c => c.Path == "/elsewhere");
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("not an address")]
    public async Task Only_web_addresses_are_accepted(string address)
    {
        await using var services = await StartAsync();

        await using var scope = services.CreateAsyncScope();
        var (_, failure) = await scope.ServiceProvider.GetRequiredService<ConnectedSources>().AddKarakeepAsync(Guid.NewGuid(), address, ApiKey, CancellationToken.None);

        Assert.Equal(nameof(FetchError.InvalidUrl), failure?.Reason);
        Assert.Empty(_calls);
    }

    public void Dispose()
    {
        _server.Dispose();
        _host.Dispose();
    }

    private Task<ServiceProvider> StartAsync() => _host.StartAsync(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}" });

    private async Task<(Source? Source, SourceResult.Failure? Failure)> AddAsync(IServiceProvider services, string key)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "reader" };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return await scope.ServiceProvider.GetRequiredService<ConnectedSources>().AddKarakeepAsync(user.Id, _server.Url("/").ToString(), key, CancellationToken.None);
    }

    private static async Task<SourceResult> FetchAsync(IServiceProvider services, Source source)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ISource>().Single(s => s.Kind == SourceKind.Karakeep).FetchAsync(source, CancellationToken.None);
    }

    private static async Task<SourceAcknowledgement> AcknowledgeAsync(IServiceProvider services, Source source, IReadOnlyList<string> ids)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ISource>().Single(s => s.Kind == SourceKind.Karakeep).AcknowledgeAsync(source, ids, CancellationToken.None);
    }
}
