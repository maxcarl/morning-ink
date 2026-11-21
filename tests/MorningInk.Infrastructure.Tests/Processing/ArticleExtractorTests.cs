using AngleSharp.Html.Parser;
using MorningInk.Infrastructure.Processing;

namespace MorningInk.Infrastructure.Tests.Processing;

/// <summary>
/// Comparison tests on saved pages. Expected sentences come from the article body and unwanted ones from the
/// page's navigation, footer or comments; both were chosen with an independent HTML parser (Python).
/// </summary>
public class ArticleExtractorTests
{
    private static readonly ArticleSanitizer Sanitizer = new();

    [Theory]
    [InlineData("conversation-iowa-lead.html", "The detonations were briefly delayed by the backlash an", "Consent preferences")]
    [InlineData("conversation-snl-healthcare.html", "According to a 2025 report from AARP and the National A", "Consent preferences")]
    [InlineData("nasa-curiosity-samples.html", "This Nov. 16 collage shows 49 drill holes captured by N", "NASA Official")]
    [InlineData("nasa-seed-funding.html", "This SBIR Phase I award, led by Intelligent Fiber Optic", "NASA Official")]
    [InlineData("netzpolitik-bundespolizei.html", "netzpolitik.org hat bereits mehrfach über Webloc und de", "Wir kämpfen für digitale Freiheitsrechte")]
    [InlineData("netzpolitik-kids-act.html", "Das und mehr geht aus der 27-seitigen Stellungnahme vom", "Wir kämpfen für digitale Freiheitsrechte")]
    [InlineData("netzpolitik-steuerluecke.html", "Hinzu kommt: Die Steuerlücke wächst. Gemeint ist die Su", "offenbar meinen ihre Gesetze erlaubten ihnen ALLES")]
    [InlineData("wikipedia-de-e-book-reader.html", "Ungeklärt ist weiterhin, ob die Möglichkeit bestehen wi", "Abgerufen von")]
    [InlineData("wikipedia-de-zeitung.html", "Nach der Studie World Press Trends 2007 der World Assoc", "Abgerufen von")]
    [InlineData("wikipedia-en-rss.html", "The extension mechanisms make it possible for each bran", "Retrieved from")]
    public void Extracts_the_article_without_page_chrome(string file, string expected, string unwanted)
    {
        var html = ArticleExtractor.Extract(Read(file), new Uri("https://example.com/article"));

        Assert.NotNull(html);
        var text = ToText(Sanitizer.Html(html, new Uri("https://example.com/article")));
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain(unwanted, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_without_an_article_yields_nothing()
    {
        var html = ArticleExtractor.Extract("<html><body><nav><a href='/'>Home</a></nav></body></html>"u8.ToArray(), new Uri("https://example.com/"));

        Assert.Null(html);
    }

    private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "Pages", file));

    // Soft hyphens (netzpolitik) and line breaks would otherwise split the expected sentences.
    private static string ToText(string html) =>
        string.Join(' ', new HtmlParser().ParseDocument(html).Body!.TextContent.Replace("­", "", StringComparison.Ordinal) // ! is safe: parsed documents always have a body
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
