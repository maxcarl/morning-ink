using SmartReader;

namespace MorningInk.Infrastructure.Processing;

/// <summary>Finds the article in a web page and drops navigation, ads, footers and comments.</summary>
internal static class ArticleExtractor
{
    /// <summary>Returns the article as (unsanitized) HTML, or null if the page has no readable article.</summary>
    /// <param name="html">Page bytes; the page's own charset declaration decides the encoding.</param>
    /// <param name="url">Address of the page; relative links in the article resolve against it.</param>
    public static string? Extract(byte[] html, Uri url)
    {
        // The content is handed over, so SmartReader never downloads anything itself; all fetching goes
        // through the safe HTTP client.
        using var stream = new MemoryStream(html);
        var article = new Reader(url.ToString(), stream).GetArticle();
        return article.IsReadable && !string.IsNullOrWhiteSpace(article.Content) ? article.Content : null;
    }
}
