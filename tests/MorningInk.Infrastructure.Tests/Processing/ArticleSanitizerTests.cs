using MorningInk.Infrastructure.Processing;

namespace MorningInk.Infrastructure.Tests.Processing;

public class ArticleSanitizerTests
{
    private static readonly Uri BaseUri = new("https://example.com/news/article");
    private readonly ArticleSanitizer _sanitizer = new();

    [Fact]
    public void Newsletter_html_is_cleaned_with_its_message_id_as_base()
    {
        var html = _sanitizer.Html("<table><tr><td><p>Hallo <a href=\"https://brief.example.com/x\">mehr</a><a href=\"relativ\">kaputt</a></p><script>x()</script></td></tr></table>", new Uri("mid:abc%40example.com"));

        Assert.Contains("<a href=\"https://brief.example.com/x\">mehr</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("script", html, StringComparison.Ordinal);
        Assert.DoesNotContain("mid:", html, StringComparison.Ordinal); // a relative link has nothing to point to
    }

    [Theory]
    [InlineData("<p>Hi<script>alert(1)</script></p>", "alert")]
    [InlineData("<img src=\"https://example.com/a.png\" onerror=\"alert(1)\">", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript")]
    [InlineData("<p style=\"background:url(https://tracker.example/p.gif)\">x</p>", "tracker")]
    [InlineData("<iframe src=\"https://example.com/embed\"></iframe>", "iframe")]
    [InlineData("<img src=\"data:image/svg+xml;base64,PHN2Zz4=\">", "data:")]
    [InlineData("<style>body{background:url(https://tracker.example/x)}</style><p>x</p>", "tracker")]
    [InlineData("<form action=\"https://evil.example\"><input name=\"q\"></form>", "form")]
    public void Dangerous_markup_is_removed(string html, string forbidden) =>
        Assert.DoesNotContain(forbidden, _sanitizer.Html(html, BaseUri), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Article_markup_is_kept_and_relative_links_become_absolute()
    {
        var html = _sanitizer.Html("<div class=\"x\"><h2>Title</h2><p>Text with <a href=\"/other\">link</a> and <em>emphasis</em>.</p><img src=\"img.png\" alt=\"Bild\"></div>", BaseUri);

        Assert.Equal("<h2>Title</h2><p>Text with <a href=\"https://example.com/other\">link</a> and <em>emphasis</em>.</p><img src=\"https://example.com/news/img.png\" alt=\"Bild\">", html);
    }

    [Theory]
    [InlineData("Breaking: <b>Big</b> news", "Breaking: Big news")]
    [InlineData("Tom &amp; Jerry", "Tom & Jerry")]
    [InlineData("  <script>alert(1)</script>Safe  ", "Safe")]
    public void Text_has_no_markup(string input, string expected) =>
        Assert.Equal(expected, _sanitizer.Text(input));
}
