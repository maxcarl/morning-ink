using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Wikipedia;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Wikipedia;

/// <summary>The article of the day against saved responses: German and English exist, French has a bad link, Polish none.</summary>
public sealed class WikipediaFeaturedArticleTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 24);

    private readonly LocalServer _server = new(async context =>
    {
        var file = context.Request.Url!.AbsolutePath switch // ! is safe: a received request always has a URL
        {
            "/feed/de/featured/2026/11/24" => "featured-de.json",
            "/feed/en/featured/2026/11/24" => "featured-en.json",
            "/feed/fr/featured/2026/11/24" => "featured-fr.json",
            _ => null,
        };
        if (file is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Wikipedia", file)));
    });

    [Fact]
    public async Task The_article_of_the_day_comes_as_plain_text_in_the_countrys_language()
    {
        var article = await Featured().ArticleAsync(Day, "AT", CancellationToken.None);

        Assert.NotNull(article);
        Assert.Equal("Beispielhausen", article.Title);
        Assert.Equal(["Beispielhausen ist eine kleine Gemeinde am Fluss <Musterbach>.", "Sie ist für ihre alte Brücke bekannt."], article.Paragraphs); // the plain extract, not the HTML one
        Assert.Equal(new Uri("https://de.wikipedia.org/wiki/Beispielhausen"), article.Url);
    }

    [Theory]
    [InlineData("PL")] // a Wikipedia without an article of the day
    [InlineData("FR")] // a link that is no web page
    [InlineData(null)]
    public async Task Without_a_usable_article_in_the_countrys_language_it_comes_in_English(string? country)
    {
        var article = await Featured().ArticleAsync(Day, country, CancellationToken.None);

        Assert.Equal("Sampletown", article?.Title);
    }

    [Fact]
    public async Task An_unavailable_service_gives_nothing()
    {
        Assert.Null(await Featured("/missing").ArticleAsync(Day, "DE", CancellationToken.None));
    }

    public void Dispose() => _server.Dispose();

    private IFeaturedArticle Featured(string path = "/feed") => new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = Path.GetTempPath(),
                ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}",
                ["Wikipedia:FeedUrl"] = _server.Url(path).ToString(),
            })
            .Build())
        .BuildServiceProvider()
        .GetRequiredService<IFeaturedArticle>();
}
