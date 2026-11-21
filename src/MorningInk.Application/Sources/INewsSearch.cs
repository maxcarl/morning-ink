namespace MorningInk.Application.Sources;

/// <summary>Finds recent news for search terms; Google News now, a self-hosted SearXNG later.</summary>
public interface INewsSearch
{
    /// <summary>Recent articles matching any of the terms; expected problems come back as <see cref="SourceResult.Failure"/>.</summary>
    Task<SourceResult> SearchAsync(NewsQuery query, CancellationToken cancellationToken);
}

/// <param name="Terms">Phrases of which any must match, e.g. a town and its district.</param>
/// <param name="MaxAge">How far back to search.</param>
/// <param name="Country">ISO country code whose news edition to search, e.g. "DE"; null uses the configured default.</param>
public sealed record NewsQuery(IReadOnlyList<string> Terms, TimeSpan MaxAge, string? Country = null);
