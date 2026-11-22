using System.Text;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Sources;

namespace MorningInk.Infrastructure.Tests.Sources;

public class FeedParserTests
{
    private static readonly Uri BaseUri = new("https://example.com/feed.xml");

    // Expected counts and first titles were taken from the saved feeds with an independent parser (Python).
    [Theory]
    [InlineData("tagesschau-rss2.xml", 40, "Riemannsche Vermutung: Mathe-Jahrhundertproblem mit KI gelös")]
    [InlineData("heise-rss2.xml", 150, "Internet Archive soll Gewerkschaft bekommen")]
    [InlineData("golem-rdf.xml", 40, "Anzeige: IT-Jobs in Administration, Data Engineering und Sec")]
    [InlineData("github-releases-atom.xml", 10, ".NET 9.0.23")]
    public void Real_feeds_yield_all_articles(string file, int count, string firstTitle)
    {
        var articles = Parse(file);

        Assert.Equal(count, articles.Count);
        Assert.StartsWith(firstTitle, articles[0].Title, StringComparison.Ordinal);
        Assert.All(articles, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
            Assert.True(a.Url.Scheme is "http" or "https");
            Assert.NotNull(a.PublishedAt);
        });
    }

    [Fact]
    public void Latin1_feed_keeps_umlauts()
    {
        var articles = Parse("golem-rdf.xml");

        Assert.Contains(articles, a => a.Title == "Anthropic: Mathematiker will mit Claude Riemann-Vermutung gelöst haben");
    }

    [Fact]
    public void Atom_entries_use_the_alternate_link_and_content()
    {
        var release = Parse("github-releases-atom.xml")[0];

        Assert.StartsWith("https://github.com/dotnet/runtime/releases/tag/", release.Url.ToString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(release.Content));
    }

    [Theory]
    [InlineData("xxe.xml")]
    [InlineData("billion-laughs.xml")]
    public void Entity_attacks_are_rejected(string file)
    {
        var result = FeedParser.Parse(Read(file), BaseUri);

        Assert.IsType<SourceResult.Failure>(result);
    }

    [Fact]
    public void Categories_are_read_from_rss_dublin_core_and_atom()
    {
        var rss = Encoding.UTF8.GetBytes("""
            <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/"><channel><title>T</title>
              <item><title>A</title><link>https://example.com/a</link><category> Advertorial </category><dc:subject>Technik</dc:subject></item>
            </channel></rss>
            """);
        var atom = Encoding.UTF8.GetBytes("""
            <feed xmlns="http://www.w3.org/2005/Atom"><title>T</title>
              <entry><title>B</title><link href="https://example.com/b"/><category term="sponsored" label="Sponsored"/></entry>
            </feed>
            """);

        Assert.Equal(["Advertorial", "Technik"], Assert.IsType<SourceResult.Success>(FeedParser.Parse(rss, BaseUri)).Articles[0].Categories);
        Assert.Equal(["sponsored", "Sponsored"], Assert.IsType<SourceResult.Success>(FeedParser.Parse(atom, BaseUri)).Articles[0].Categories);
    }

    [Fact]
    public void Html_page_is_not_a_feed()
    {
        var result = FeedParser.Parse("<html><body>Hello</body></html>"u8.ToArray(), BaseUri);

        Assert.IsType<SourceResult.Failure>(result);
    }

    [Fact]
    public void Edge_cases_are_handled()
    {
        var articles = Parse("edge-cases.xml");

        Assert.Equal(["Relative link", "Broken date is kept without date", "Link only in guid"], articles.Select(a => a.Title));
        Assert.Equal(new Uri("https://example.com/articles/relative"), articles[0].Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 6, 0, 0, TimeSpan.Zero), articles[0].PublishedAt);
        Assert.Null(articles[1].PublishedAt);
        Assert.Equal(new Uri("https://example.com/from-guid"), articles[2].Url);
        Assert.Equal("Jane Doe", articles[2].Author);
    }

    [Theory]
    [InlineData("Sat, 21 Nov 2026 18:44:34 +0100", "2026-11-21T18:44:34+01:00")]
    [InlineData("Sat, 21 Nov 2026 18:44:34 GMT", "2026-11-21T18:44:34+00:00")]
    [InlineData("21 Nov 2026 18:44 -0500", "2026-11-21T18:44:00-05:00")]
    [InlineData("2026-11-21T19:51:02+01:00", "2026-11-21T19:51:02+01:00")]
    [InlineData("2026-11-21T19:51:02Z", "2026-11-21T19:51:02+00:00")]
    public void Common_date_formats_are_understood(string text, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), FeedParser.ParseDate(text));

    private static List<FetchedArticle> Parse(string file) =>
        Assert.IsType<SourceResult.Success>(FeedParser.Parse(Read(file), BaseUri)).Articles.ToList();

    private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Feeds", file));
}
