using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>FreshRSS through its Google Reader API, played by a local server with answers in FreshRSS's format.</summary>
public sealed class FreshRssSourceTests : IDisposable
{
    private const string User = "reader";
    private const string ApiPassword = "api-password";
    private const string Auth = "reader/0123456789abcdef";

    private readonly TestHost _host = new();
    private readonly ConcurrentQueue<(string Path, string? Authorization, string Body)> _calls = new();
    private readonly LocalServer _server;

    public FreshRssSourceTests() => _server = new LocalServer(async context =>
    {
        var request = context.Request;
        var body = await new StreamReader(request.InputStream).ReadToEndAsync();
        var url = request.Url!; // ! is safe: a received request always has a URL
        var (path, port) = (url.AbsolutePath, url.Port);
        _calls.Enqueue((url.PathAndQuery, request.Headers["Authorization"], body));
        var authorized = request.Headers["Authorization"] == $"GoogleLogin auth={Auth}";
        var (status, answer) = path switch
        {
            "/api/greader.php/accounts/ClientLogin" when request.HttpMethod == "POST" && body == $"Email={User}&Passwd={ApiPassword}" =>
                (200, $"SID={Auth}\nLSID=null\nAuth={Auth}\n"),
            "/api/greader.php/accounts/ClientLogin" => (401, "Unauthorized!"),
            "/moved/api/greader.php/accounts/ClientLogin" => (302, string.Empty),
            "/api/greader.php/reader/api/0/tag/list" when authorized => (200, Read("tag-list.json")),
            "/api/greader.php/reader/api/0/stream/contents/user/-/label/Technik" when authorized => (200, Read("stream-technik.json")),
            "/api/greader.php/reader/api/0/token" when authorized => (200, "write-token\n"),
            "/api/greader.php/reader/api/0/edit-tag" when authorized && request.HttpMethod == "POST" => (200, "OK"),
            _ => (401, "Unauthorized!"),
        };
        context.Response.StatusCode = status;
        if (status == 302)
        {
            context.Response.RedirectLocation = $"http://localhost:{port}/elsewhere";
        }

        await context.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(answer));
    });

    [Fact]
    public async Task Connecting_lists_the_categories_and_a_wrong_password_says_so()
    {
        await using var services = await StartAsync();

        var connected = await ConnectAsync(services, Address, ApiPassword);
        var wrong = await ConnectAsync(services, Address, "wrong");

        var categories = Assert.IsType<FreshRssConnection.Connected>(connected).Categories;
        Assert.Equal([("user/-/label/Technik", "Technik"), ("user/-/label/Wissenschaft", "Wissenschaft")], categories.Select(c => (c.Stream, c.Name)));
        Assert.Equal("FreshRssLogin", Assert.IsType<FreshRssConnection.Failed>(wrong).Failure.Reason);
    }

    [Fact]
    public async Task Address_of_the_instance_finds_its_api()
    {
        await using var services = await StartAsync();

        var connected = Assert.IsType<FreshRssConnection.Connected>(await ConnectAsync(services, _server.Url("/").ToString(), ApiPassword));

        Assert.Equal(_server.Url("/api/greader.php"), connected.Api);
    }

    [Fact]
    public async Task A_category_brings_its_unread_articles_with_full_text_and_web_links_only()
    {
        await using var services = await StartAsync();
        var source = await AddAsync(services);

        var result = await FetchAsync(services, source);

        var articles = Assert.IsType<SourceResult.Success>(result).Articles;
        Assert.Equal(["Neuer Raspberry Pi vorgestellt", "Ohne Autor, mit Herkunft"], articles.Select(a => a.Title));
        var first = articles[0];
        Assert.Equal("tag:google.com,2005:reader/item/0006299a1b2c3d4e", first.Id); // the id FreshRSS marks read later
        Assert.Equal(new Uri("https://www.example.com/raspberry-pi-6"), first.Url);
        Assert.Equal("Erika Muster", first.Author);
        Assert.Contains("mehr</b> Speicher", first.Content, StringComparison.Ordinal);
        Assert.Equal("Beispiel Blog", articles[1].Author); // the feed's name when there is no author
        var stream = Assert.Single(_calls, c => c.Path.Contains("/stream/contents/", StringComparison.Ordinal)).Path;
        Assert.Contains("xt=user%2F-%2Fstate%2Fcom.google%2Fread", stream, StringComparison.Ordinal); // unread only
    }

    [Fact]
    public async Task Delivered_articles_are_marked_read_in_freshrss()
    {
        await using var services = await StartAsync();
        var source = await AddAsync(services);

        var result = await AcknowledgeAsync(services, source, ["tag:google.com,2005:reader/item/a", "tag:google.com,2005:reader/item/b"]);

        Assert.Equal(new SourceAcknowledgement(2, null), result);
        var edit = Assert.Single(_calls, c => c.Path.EndsWith("/edit-tag", StringComparison.Ordinal)).Body;
        Assert.Contains("a=user%2F-%2Fstate%2Fcom.google%2Fread", edit, StringComparison.Ordinal);
        Assert.Contains("i=tag%3Agoogle.com%2C2005%3Areader%2Fitem%2Fa", edit, StringComparison.Ordinal);
        Assert.Contains("i=tag%3Agoogle.com%2C2005%3Areader%2Fitem%2Fb", edit, StringComparison.Ordinal);
        Assert.Contains("T=write-token", edit, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_marked_read_when_the_source_says_so()
    {
        await using var services = await StartAsync();
        var source = await AddAsync(services);
        source.MarkReadAfterDelivery = false;

        var result = await AcknowledgeAsync(services, source, ["tag:google.com,2005:reader/item/a"]);

        Assert.Equal(SourceAcknowledgement.Nothing, result);
        Assert.DoesNotContain(_calls, c => c.Path.Contains("/edit-tag", StringComparison.Ordinal) || c.Path.Contains("ClientLogin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_never_follows_a_redirect_with_the_password()
    {
        await using var services = await StartAsync();

        var result = await ConnectAsync(services, _server.Url("/moved/api/greader.php").ToString(), ApiPassword);

        Assert.IsType<FreshRssConnection.Failed>(result);
        Assert.DoesNotContain(_calls, c => c.Path == "/elsewhere");
    }

    public void Dispose()
    {
        _server.Dispose();
        _host.Dispose();
    }

    private string Address => _server.Url("/api/greader.php").ToString();

    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "FreshRss", file));

    private Task<ServiceProvider> StartAsync() => _host.StartAsync(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}" });

    private static async Task<FreshRssConnection> ConnectAsync(IServiceProvider services, string address, string password)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ConnectedSources>().ConnectFreshRssAsync(address, User, password, CancellationToken.None);
    }

    // As the sources page adds it: one user, the "Technik" category.
    private async Task<Source> AddAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "reader" };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        var sources = await scope.ServiceProvider.GetRequiredService<ConnectedSources>().AddFreshRssAsync(user.Id, new Uri(Address), User, ApiPassword, [new FreshRssCategory("user/-/label/Technik", "Technik")], CancellationToken.None);
        return Assert.Single(sources);
    }

    private static async Task<SourceResult> FetchAsync(IServiceProvider services, Source source)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ISource>().Single(s => s.Kind == SourceKind.FreshRss).FetchAsync(source, CancellationToken.None);
    }

    private static async Task<SourceAcknowledgement> AcknowledgeAsync(IServiceProvider services, Source source, IReadOnlyList<string> ids)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ISource>().Single(s => s.Kind == SourceKind.FreshRss).AcknowledgeAsync(source, ids, CancellationToken.None);
    }
}
