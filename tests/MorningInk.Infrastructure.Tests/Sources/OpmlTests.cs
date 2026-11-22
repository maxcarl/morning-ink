using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>OPML import and export on the sources page: moving from FreshRSS, Feedly or another reader.</summary>
public sealed class OpmlTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task Import_takes_the_feeds_of_all_folders_and_skips_duplicates_and_unsafe_addresses()
    {
        await using var services = await _host.StartAsync();
        var userId = await CreateUserAsync(services);
        await using (var scope = services.CreateAsyncScope())
        {
            var added = await Catalog(scope).AddFeedAsync(userId, "Schon da", "https://existing.example.com/feed/", fetchFullText: true, CancellationToken.None);
            Assert.IsType<SourceSaveResult.Saved>(added);
        }

        var result = await ImportAsync(services, userId, File.OpenRead(Fixture("freshrss-export.opml")));

        Assert.Equal(new OpmlImport(Added: 4, Duplicates: 2, Invalid: 3), result);
        var names = (await ListAsync(services, userId)).Select(s => (s.Name, s.Url)).Order().ToList();
        Assert.Equal(
            [
                ("Beispiel Blog", "https://blog.example.com/feed.xml"),
                ("noname.example.com", "https://noname.example.com/feed"), // no name in the file: the host
                ("Nur mit Titel", "https://science.example.net/atom"),
                ("Schon da", "https://existing.example.com/feed/"),
                ("Technik-News", "https://news.example.org/rss"),
            ],
            names);
    }

    [Fact]
    public async Task Imported_feeds_start_like_feeds_added_by_hand()
    {
        await using var services = await _host.StartAsync();
        var userId = await CreateUserAsync(services);

        await ImportAsync(services, userId, File.OpenRead(Fixture("freshrss-export.opml")));

        var source = (await ListAsync(services, userId))[0];
        Assert.Equal((SourceKind.Rss, true, userId), (source.Kind, source.FetchFullText, source.UserId));
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<rss><channel/></rss>")]
    public async Task Anything_but_opml_is_refused(string content)
    {
        await using var services = await _host.StartAsync();
        var userId = await CreateUserAsync(services);

        Assert.Null(await ImportAsync(services, userId, Text(content)));
        Assert.Empty(await ListAsync(services, userId));
    }

    [Fact]
    public async Task Entities_are_never_resolved()
    {
        await using var services = await _host.StartAsync();
        var userId = await CreateUserAsync(services);
        const string Xxe = """
            <?xml version="1.0"?>
            <!DOCTYPE opml [<!ENTITY secret SYSTEM "file:///etc/hostname">]>
            <opml version="2.0"><body><outline text="&secret;" xmlUrl="https://example.com/feed"/></body></opml>
            """;

        await ImportAsync(services, userId, Text(Xxe));

        Assert.DoesNotContain(await ListAsync(services, userId), s => s.Name.Length > 0 && s.Name != "example.com");
    }

    [Fact]
    public async Task Too_big_a_file_is_refused_before_it_is_read()
    {
        await using var services = await _host.StartAsync();
        var userId = await CreateUserAsync(services);

        var big = new MemoryStream(new byte[Opml.MaxBytes + 1]);

        Assert.Null(await ImportAsync(services, userId, big));
    }

    [Fact]
    public async Task Export_has_the_users_own_feeds_only_and_imports_again()
    {
        await using var services = await _host.StartAsync();
        var userId = await CreateUserAsync(services);
        var otherId = await CreateUserAsync(services, "other");
        await ImportAsync(services, userId, File.OpenRead(Fixture("freshrss-export.opml")));
        await using (var scope = services.CreateAsyncScope())
        {
            await Catalog(scope).AddFeedAsync(otherId, "Fremd", "https://other.example.com/feed", fetchFullText: true, CancellationToken.None);
            await Catalog(scope).AddLocalAsync(userId, "Lokales München", "München", "DE", CancellationToken.None);
        }

        string exported;
        await using (var scope = services.CreateAsyncScope())
        {
            exported = await Catalog(scope).ExportOpmlAsync(userId, CancellationToken.None);
        }

        Assert.DoesNotContain("other.example.com", exported, StringComparison.Ordinal);
        Assert.DoesNotContain("München", exported, StringComparison.Ordinal); // local news is no feed
        var thirdId = await CreateUserAsync(services, "third");
        Assert.Equal(new OpmlImport(5, 0, 0), await ImportAsync(services, thirdId, Text(exported))); // all five of the file, as none was there yet
    }

    public void Dispose() => _host.Dispose();

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Opml", name);

    private static MemoryStream Text(string content) => new(Encoding.UTF8.GetBytes(content));

    private static SourceCatalog Catalog(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<SourceCatalog>();

    private static async Task<OpmlImport?> ImportAsync(IServiceProvider services, Guid userId, Stream file)
    {
        await using var scope = services.CreateAsyncScope();
        await using (file)
        {
            return await Catalog(scope).ImportOpmlAsync(userId, file, CancellationToken.None);
        }
    }

    private static async Task<IReadOnlyList<Source>> ListAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        return await Catalog(scope).ListAsync(userId, CancellationToken.None);
    }

    private static async Task<Guid> CreateUserAsync(IServiceProvider services, string name = "reader")
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = name };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
