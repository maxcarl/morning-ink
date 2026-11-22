using System.Text.RegularExpressions;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Processing;

/// <summary>A source's rules for which articles may enter an issue.</summary>
/// <remarks>
/// <see cref="Admits"/> looks at title and teaser and runs before the full text is fetched, so a left-out article costs no
/// download and no place in the section's limit. <see cref="IsLongEnough"/> needs the full text and runs after it.
/// </remarks>
public static partial class ArticleFilter
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether the keyword appears in headline or teaser, as a whole word, ignoring case and ß/ss.</summary>
    public static bool Mentions(FetchedArticle article, string keyword) => Contains(HeadlineAndTeaser(article), keyword);

    public static bool Admits(FetchedArticle article, Source source)
    {
        var text = HeadlineAndTeaser(article);
        if (source.SkipLiveTickers && LiveTicker().IsMatch(article.Title))
        {
            return false;
        }

        if (source.ExcludeKeywords.Any(keyword => Contains(text, keyword)))
        {
            return false;
        }

        return source.IncludeKeywords.Count == 0 || source.IncludeKeywords.Any(keyword => Contains(text, keyword));
    }

    public static bool IsLongEnough(FetchedArticle article, Source source) =>
        source.MinWords is not { } minimum
        || PlainText(article.Content ?? article.Summary).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= minimum;

    private static string HeadlineAndTeaser(FetchedArticle article) => $"{article.Title} {PlainText(article.Summary)}";

    // A keyword counts as a whole word or phrase: "Promi" does not match "Promis". Case and ß/ss do not matter.
    private static bool Contains(string text, string keyword)
    {
        var word = keyword.Trim();
        return word.Length > 0 && Regex.IsMatch(
            Fold(text),
            $@"(?<!\w){Regex.Escape(Fold(word))}(?!\w)",
            RegexOptions.CultureInvariant,
            MatchTimeout);
    }

    private static string Fold(string text) => text.ToLowerInvariant().Replace("ß", "ss", StringComparison.Ordinal);

    // Good enough to search words in; the HTML is cleaned properly before it ever reaches an issue.
    private static string PlainText(string? html) => html is null ? string.Empty : System.Net.WebUtility.HtmlDecode(Tags().Replace(html, " "));

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Tags();

    // "Liveticker", "Live-Ticker", "Liveblog", "LIVE:" at the start, and the "+++ … +++" style of breaking news tickers.
    [GeneratedRegex(@"\blive[\s-]?(ticker|blog)|^\s*live\s*[:|–-]|\+\+\+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LiveTicker();
}
