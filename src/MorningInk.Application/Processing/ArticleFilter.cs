using System.Text.RegularExpressions;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Processing;

/// <summary>Why <see cref="ArticleFilter"/> leaves an article out.</summary>
public enum FilterReason
{
    LiveTicker,
    Ad,
    Media,
    Excluded,
    NotIncluded,
    TooShort,
}

/// <summary>An article left out, and why; <paramref name="Keyword"/> is set for <see cref="FilterReason.Excluded"/>.</summary>
public sealed record FilterVerdict(FilterReason Reason, string? Keyword = null);

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

    public static bool Admits(FetchedArticle article, Source source) => Reject(article, source) is null;

    /// <summary>Why the source's rules leave the article out, or null if it may enter; the keyword is the one that decided.</summary>
    public static FilterVerdict? Reject(FetchedArticle article, Source source)
    {
        var text = HeadlineAndTeaser(article);
        if (source.SkipLiveTickers && LiveTicker().IsMatch(article.Title))
        {
            return new(FilterReason.LiveTicker);
        }

        if (source.SkipAds && IsMarkedAsAd(article))
        {
            return new(FilterReason.Ad);
        }

        if (source.SkipMedia && IsVideoOrAudio(article))
        {
            return new(FilterReason.Media);
        }

        if (source.ExcludeKeywords.FirstOrDefault(keyword => Contains(text, keyword)) is { } excluded)
        {
            return new(FilterReason.Excluded, excluded);
        }

        return source.IncludeKeywords.Count == 0 || source.IncludeKeywords.Any(keyword => Contains(text, keyword))
            ? null
            : new(FilterReason.NotIncluded);
    }

    public static bool IsLongEnough(FetchedArticle article, Source source) =>
        source.MinWords is not { } minimum
        || PlainText(article.Content ?? article.Summary).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length >= minimum;

    /// <summary>Whether the publisher marks the article as advertising, by title prefix or bracket, category or address.</summary>
    /// <remarks>Only marks count, never the word in running text, so articles about displays or ad rules stay.</remarks>
    public static bool IsMarkedAsAd(FetchedArticle article) =>
        AdTitle().IsMatch(article.Title)
        || (article.Categories ?? []).Any(category => AdMarks.Contains(category))
        || article.Url.AbsolutePath.Split('/').Any(segment => AdSections.Contains(Path.GetFileNameWithoutExtension(segment)));

    /// <summary>Whether the item is a video or a programme to listen to, which an e-reader cannot play.</summary>
    /// <remarks>
    /// Told by an attached video or audio file, a "Video:" title, or the address: a media section like
    /// "/multimedia/" or "/podcast/", or a page named "video-…" as on tagesschau.de.
    /// </remarks>
    public static bool IsVideoOrAudio(FetchedArticle article)
    {
        if (article.MediaType?.Split('/')[0].ToLowerInvariant() is "video" or "audio" || MediaTitle().IsMatch(article.Title))
        {
            return true;
        }

        var segments = article.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.SkipLast(1).Any(MediaSections.Contains)
            || (segments.LastOrDefault() is { } page && MediaPage().IsMatch(page));
    }

    private static string HeadlineAndTeaser(FetchedArticle article) => $"{article.Title} {PlainText(article.Summary)}";

    // A keyword counts as a whole word or phrase: "Promi" does not match "Promis". A trailing "*" takes every word
    // that starts so ("Fußball*" also finds "Fußballer"), which German compounds need. Case and ß/ss do not matter.
    private static bool Contains(string text, string keyword)
    {
        var word = keyword.Trim();
        var wordStart = word.EndsWith('*');
        word = word.TrimEnd('*').TrimEnd();
        return word.Length > 0 && Regex.IsMatch(
            Fold(text),
            $@"(?<!\w){Regex.Escape(Fold(word))}{(wordStart ? string.Empty : @"(?!\w)")}",
            RegexOptions.CultureInvariant,
            MatchTimeout);
    }

    private static string Fold(string text) => text.ToLowerInvariant().Replace("ß", "ss", StringComparison.Ordinal);

    // Good enough to search words in; the HTML is cleaned properly before it ever reaches an issue.
    private static string PlainText(string? html) => html is null ? string.Empty : System.Net.WebUtility.HtmlDecode(Tags().Replace(html, " "));

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Tags();

    // Category names publishers use for paid content, compared whole and ignoring case.
    private static readonly HashSet<string> AdMarks = new(
        ["Anzeige", "Werbung", "Gesponsert", "Sponsored", "Sponsored Post", "Sponsored Content", "Advertorial", "Advertisement", "Promoted", "Paid Post", "Partner Content"],
        StringComparer.OrdinalIgnoreCase);

    // Address segments of sponsored sections, e.g. https://example.com/sponsored/….
    private static readonly HashSet<string> AdSections = new(
        ["anzeige", "sponsored", "sponsored-post", "sponsored-content", "advertorial", "advertorials", "promoted", "paid-post", "partner-content"],
        StringComparer.OrdinalIgnoreCase);

    // A mark at the start followed by ":" or "|" ("Anzeige: …", "Sponsored | …"), or in brackets anywhere ("… (Werbung)").
    [GeneratedRegex(@"^\s*[\[(]?\s*(anzeige|werbung|gesponsert|sponsored(\s+(post|content))?|advertorial|promoted|paid\s+post)\s*[\])]?\s*[:|]|[\[(]\s*(anzeige|werbung|gesponsert|sponsored|advertorial|promoted)\s*[\])]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex AdTitle();

    // Sections that hold programmes, not articles; only folders count, so a page named "video-games" is no section.
    private static readonly HashSet<string> MediaSections = new(
        ["multimedia", "video", "videos", "audio", "audios", "podcast", "podcasts", "mediathek", "sendung", "sendungen"],
        StringComparer.OrdinalIgnoreCase);

    // "video-1530922.html", "audio-123.html": the player pages of public broadcasters.
    [GeneratedRegex(@"^(video|audio)-\d+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex MediaPage();

    // "Video: …", "Audio: …", "Podcast | …" at the start.
    [GeneratedRegex(@"^\s*(video|audio|podcast)\s*[:|]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex MediaTitle();

    // "Liveticker", "Live-Ticker", "Liveblog", "LIVE:" at the start, and the "+++ … +++" style of breaking news tickers.
    [GeneratedRegex(@"\blive[\s-]?(ticker|blog)|^\s*live\s*[:|–-]|\+\+\+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LiveTicker();
}
