namespace MorningInk.Domain.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://www.heise.de/news/artikel-123.html", "http://heise.de/news/artikel-123.html")] // scheme and www
    [InlineData("https://Example.COM/Path", "https://example.com/Path")] // host case, path case kept
    [InlineData("https://example.com:443/a", "https://example.com/a")] // default port
    [InlineData("https://example.com/a/", "https://example.com/a")] // trailing slash
    [InlineData("https://example.com/a#comments", "https://example.com/a")] // fragment
    [InlineData("https://example.com/a?utm_source=rss&utm_medium=feed", "https://example.com/a")]
    [InlineData("https://example.com/a?id=5&fbclid=abc", "https://example.com/a?id=5")]
    [InlineData("https://www.heise.de/news/x.html?wt_mc=rss.red.ho.ho.atom.beitrag.beitrag", "https://www.heise.de/news/x.html")]
    [InlineData("https://example.com/a?b=2&a=1", "https://example.com/a?a=1&b=2")] // parameter order
    [InlineData("https://bücher.de/a", "https://xn--bcher-kva.de/a")] // internationalized domain
    public void Same_article_gets_the_same_key(string first, string second) =>
        Assert.Equal(UrlNormalizer.Normalize(new Uri(first)), UrlNormalizer.Normalize(new Uri(second)));

    [Theory]
    [InlineData("https://example.com/a", "https://example.com/b")]
    [InlineData("https://example.com/A", "https://example.com/a")] // paths are case-sensitive
    [InlineData("https://example.com/a?id=1", "https://example.com/a?id=2")] // real parameters matter
    [InlineData("https://example.com:8080/a", "https://example.com/a")]
    [InlineData("https://news.example.com/a", "https://example.com/a")]
    public void Different_articles_get_different_keys(string first, string second) =>
        Assert.NotEqual(UrlNormalizer.Normalize(new Uri(first)), UrlNormalizer.Normalize(new Uri(second)));

    [Fact]
    public void Root_keeps_its_slash() =>
        Assert.Equal("example.com/", UrlNormalizer.Normalize(new Uri("https://example.com")));
}
