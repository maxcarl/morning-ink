using MorningInk.Application.Sources;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// Local news through one's own SearXNG when the settings name one, else through Bing. Asked on every search, so a
/// change on the settings page counts from the next issue on, without a restart.
/// </summary>
internal sealed class NewsSearch(NewsSearchSettingsStore settings, SearxngNewsSearch searxng, BingNewsSearch bing) : INewsSearch
{
    public async Task<SourceResult> SearchAsync(NewsQuery query, CancellationToken cancellationToken) =>
        await settings.SearxngUrlAsync(cancellationToken) is { } searxngUrl
            ? await searxng.SearchAsync(query, searxngUrl, cancellationToken)
            : await bing.SearchAsync(query, cancellationToken);
}
