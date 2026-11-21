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

    /// <param name="BlockedHost">Set when the home-network protection blocked it: the entry an admin could allow.</param>
    public sealed record Failure(string Message, string? BlockedHost = null) : FeedDiscoveryResult;
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
            return new FeedDiscoveryResult.Failure(text.Length == 0 ? "Please enter an address." : "This is not a valid address.");
        }

        if (UrlPolicy.Check(url) is { } invalid)
        {
            return new FeedDiscoveryResult.Failure(invalid);
        }

        var page = await http.GetAsync(url, cancellationToken: cancellationToken);
        if (page is not FetchResult.Success site)
        {
            return page is FetchResult.Failure failure
                ? new FeedDiscoveryResult.Failure(failure.Message, failure.BlockedHost)
                : new FeedDiscoveryResult.Failure("The address did not answer.");
        }

        if (IsFeed(site))
        {
            return new FeedDiscoveryResult.Found([new FeedCandidate(site.FinalUri, site.FinalUri.Host)]);
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
            .Select(link => (Link: link, Ok: Uri.TryCreate(site.FinalUri, link.GetAttribute("href"), out var feed), Url: feed))
            .Where(candidate => candidate.Ok && UrlPolicy.Check(candidate.Url!) is null) // ! is safe: Ok means TryCreate set it
            .Select(candidate => new FeedCandidate(candidate.Url!, candidate.Link.GetAttribute("title")?.Trim() is { Length: > 0 } title ? title : pageTitle ?? candidate.Url!.Host))
            .DistinctBy(candidate => candidate.Url)
            .ToList();
    }

    // Many sites have a feed without linking it; the first of the usual paths that is a feed wins.
    private async Task<List<FeedCandidate>> UsualPathsAsync(Uri site, CancellationToken cancellationToken)
    {
        foreach (var path in CommonPaths)
        {
            var candidate = new Uri(site, path);
            if (await http.GetAsync(candidate, cancellationToken: cancellationToken) is FetchResult.Success response && IsFeed(response))
            {
                return [new FeedCandidate(response.FinalUri, site.Host)];
            }
        }

        return [];
    }
}
