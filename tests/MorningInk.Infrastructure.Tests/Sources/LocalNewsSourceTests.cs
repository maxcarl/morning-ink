using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>Local news: geocoding (Nominatim) and news search (Bing News RSS), both as saved real answers on a local server.</summary>
public sealed class LocalNewsSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 19, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly LocalServer _server = new(async context =>
    {
        var query = context.Request.QueryString;
        var file = context.Request.Url!.AbsolutePath switch // ! is safe: a received request always has a URL
        {
            "/search" when query["postalcode"] == "52062" => "Weather/nominatim-52062.json",
            "/search" when query["postalcode"] == "10115" => "Weather/nominatim-10115.json",
            "/search" => "Weather/nominatim-unknown.json",
            "/news/search" when query["fail"] is null => "LocalNews/bingnews-muenchen.xml",
            _ => null,
        };
        if (file is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return;
        }

        await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file)));
    });

    [Fact]
    public async Task Postal_code_brings_news_from_its_town_and_district()
    {
        var result = await FetchAsync(Local("52062"));

        var success = Assert.IsType<SourceResult.Success>(result);
        Assert.Equal(5, success.Articles.Count);
        var search = Uri.UnescapeDataString(SearchRequests().Single());
        Assert.Contains("\"Aachen\" OR \"Städteregion Aachen\"", search, StringComparison.Ordinal);
        Assert.Contains("format=rss", search, StringComparison.Ordinal);
        Assert.Contains("setlang=de", search, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Links_lead_to_the_publisher_with_teaser_and_publisher_name()
    {
        var articles = Assert.IsType<SourceResult.Success>(await FetchAsync(Local("52062"))).Articles;

        var premiere = Assert.Single(articles, a => a.Title.StartsWith("Premiere in München", StringComparison.Ordinal));
        Assert.Equal("https://www.sueddeutsche.de/muenchen/muenchen-gaertnerplatztheater-landstreicher-operette-kritik-li.3568257", premiere.Url.AbsoluteUri);
        Assert.Equal("Süddeutsche Zeitung", premiere.Author);
        Assert.Contains("Gärtnerplatztheater", premiere.Summary, StringComparison.Ordinal);
        Assert.All(articles, a => Assert.NotEqual("www.bing.com", a.Url.Host)); // full text comes from the publisher, not through Bing
    }

    [Fact]
    public async Task Each_local_source_searches_its_own_place()
    {
        await FetchAsync(Local("52062"));
        await FetchAsync(Local("10115"));

        var searches = SearchRequests().Select(Uri.UnescapeDataString).ToList();
        Assert.Contains("\"Aachen\"", searches[0], StringComparison.Ordinal);
        Assert.Contains("\"Berlin\"", searches[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_place_is_geocoded_once_and_kept_on_the_source()
    {
        var source = Local("52062");

        await FetchAsync(source);
        await FetchAsync(source);

        Assert.Equal("Aachen", source.Location?.Name);
        Assert.Single(_server.Requests, r => r.Url.Contains("/search?", StringComparison.Ordinal) && !r.Url.Contains("/news/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_place_is_a_failure_naming_the_place()
    {
        var result = await FetchAsync(Local("Xyzzyqwerty"));

        var failure = Assert.IsType<SourceResult.Failure>(result);
        Assert.Contains("Xyzzyqwerty", failure.Message, StringComparison.Ordinal);
        Assert.Equal("PlaceNotFound", failure.Reason);
        Assert.Equal(["Xyzzyqwerty"], failure.Arguments);
    }

    [Theory]
    [InlineData("AT", "setlang=de&cc=AT")]
    [InlineData("US", "setlang=en&cc=US")]
    public async Task News_come_from_the_edition_of_the_places_country(string country, string edition)
    {
        await FetchAsync(Local("10115", country));

        Assert.Contains(edition, Uri.UnescapeDataString(SearchRequests().Single()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_source_without_a_place_is_a_failure() =>
        Assert.IsType<SourceResult.Failure>(await FetchAsync(Local(null)));

    [Fact]
    public async Task Search_service_down_is_a_failure() =>
        Assert.IsType<SourceResult.Failure>(await FetchAsync(Local("52062"), searchFails: true));

    public void Dispose() => _server.Dispose();

    private Source Local(string? place, string? country = "DE") => new() { UserId = _userId, Name = $"Lokales {place}", Kind = SourceKind.Local, Place = place, Country = country };

    private List<string> SearchRequests() => _server.Requests.Select(r => r.Url).Where(u => u.Contains("/news/search", StringComparison.Ordinal)).ToList();

    private async Task<SourceResult> FetchAsync(Source source, bool searchFails = false)
    {
        var baseUrl = _server.Url("/").ToString().TrimEnd('/');
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:DataDirectory"] = Path.GetTempPath(),
                    ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}",
                    ["Weather:GeocodingUrl"] = baseUrl + "/search",
                    ["NewsSearch:BingNewsUrl"] = baseUrl + "/news/search" + (searchFails ? "?fail=1" : string.Empty),
                })
                .Build())
            .BuildServiceProvider();
        return await services.GetServices<ISource>().Single(s => s.Kind == SourceKind.Local).FetchAsync(source, CancellationToken.None);
    }
}
