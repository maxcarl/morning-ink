using AngleSharp.Html.Parser;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Sources;

/// <summary>Outcome of looking for feeds behind an address.</summary>
public abstract record FeedDiscoveryResult
{
    private FeedDiscoveryResult()
    {
    }

    /// <param name="Feeds">Feeds found; empty when the site has none.</param>
    public sealed record Found(IReadOnlyList<FeedCandidate> Feeds) : FeedDiscoveryResult;

    /// <param name="Message">Explanation in English, for logs and as a fallback.</param>
    /// <param name="BlockedHost">Set when the home-network protection blocked it: the entry an admin could allow.</param>
    /// <param name="Reason">A stable code the UI turns into words, as for <see cref="SourceResult.Failure"/>.</param>
    /// <param name="Arguments">The values the words need.</param>
    public sealed record Failure(string Message, string? BlockedHost = null, string? Reason = null, IReadOnlyList<string>? Arguments = null) : FeedDiscoveryResult
    {
        public IReadOnlyList<string> Arguments { get; init; } = Arguments ?? [];
    }
}

/// <summary>A feed found for a site.</summary>
public sealed record FeedCandidate(Uri Url, string Title);

/// <summary>
/// Finds the feed for an address a user typed: the address itself if it is a feed, else the feeds the page links
/// to (&lt;link rel="alternate"&gt;), else the usual paths like /feed.
/// </summary>
public sealed class FeedDiscovery(SafeHttpClient http)
{
    private static readonly HashSet<string> FeedTypes = new(
        ["application/rss+xml", "application/atom+xml", "application/feed+json", "application/rdf+xml"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> FormatNames = new(["rss", "atom", "feed", "rdf", "json", "rss feed", "atom feed", "rss 2.0"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[] CommonPaths = ["/feed", "/rss", "/feed.xml", "/rss.xml", "/atom.xml", "/index.xml"];

    /// <param name="input">What the user typed, e.g. "heise.de" or "https://www.heise.de/rss/heise.rdf". Untrusted.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    public async Task<FeedDiscoveryResult> DiscoverAsync(string input, CancellationToken cancellationToken)
    {
        // Users often type just the domain.
        var text = input.Trim();
        if (text.Length > 0 && !text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var url))
        {
            return text.Length == 0
                ? new FeedDiscoveryResult.Failure("Please enter an address.", Reason: "EnterAddress")
                : new FeedDiscoveryResult.Failure("This is not a valid address.", Reason: nameof(FetchError.InvalidUrl));
        }

        if (UrlPolicy.Check(url) is { } invalid)
        {
            return new FeedDiscoveryResult.Failure(invalid, Reason: nameof(FetchError.InvalidUrl));
        }

        var page = await http.GetAsync(url, cancellationToken: cancellationToken);
        if (page is not FetchResult.Success site)
        {
            return page is FetchResult.Failure failure
                ? new FeedDiscoveryResult.Failure(failure.Message, failure.BlockedHost, failure.Error.ToString(), failure.Arguments)
                : new FeedDiscoveryResult.Failure("The address did not answer.", Reason: "NoAnswer");
        }

        if (IsFeed(site))
        {
            return new FeedDiscoveryResult.Found([new FeedCandidate(site.FinalUri, FeedParser.ReadTitle(site.Content) ?? site.FinalUri.Host)]);
        }

        var linked = LinkedFeeds(site);
        return linked.Count > 0 ? new FeedDiscoveryResult.Found(linked) : new FeedDiscoveryResult.Found(await UsualPathsAsync(site.FinalUri, cancellationToken));
    }

    private static bool IsFeed(FetchResult.Success response) => FeedParser.Parse(response.Content, response.FinalUri) is SourceResult.Success;

    private static List<FeedCandidate> LinkedFeeds(FetchResult.Success site)
    {
        using var stream = new MemoryStream(site.Content);
        var document = new HtmlParser().ParseDocument(stream);
        var pageTitle = document.Title?.Trim();
        return document.QuerySelectorAll("link[rel~='alternate'][href]")
            .Where(link => FeedTypes.Contains(link.GetAttribute("type") ?? string.Empty))
            .Select(link => Uri.TryCreate(site.FinalUri, link.GetAttribute("href"), out var feed) && UrlPolicy.Check(feed) is null
                ? new FeedCandidate(feed, Describes(link.GetAttribute("title")) ?? pageTitle ?? feed.Host)
                : null)
            .OfType<FeedCandidate>()
            .DistinctBy(candidate => candidate.Url)
            .ToList();
    }

    // A link title like "RSS" or "Atom" only names the format; then the page title says more.
    private static string? Describes(string? linkTitle) =>
        linkTitle?.Trim() is { Length: > 0 } title && !FormatNames.Contains(title) ? title : null;

    // Many sites have a feed without linking it; the first of the usual paths that is a feed wins.
    private async Task<List<FeedCandidate>> UsualPathsAsync(Uri site, CancellationToken cancellationToken)
    {
        foreach (var path in CommonPaths)
        {
            var candidate = new Uri(site, path);
            if (await http.GetAsync(candidate, cancellationToken: cancellationToken) is FetchResult.Success response && IsFeed(response))
            {
                return [new FeedCandidate(response.FinalUri, FeedParser.ReadTitle(response.Content) ?? site.Host)];
            }
        }

        return [];
    }
}
