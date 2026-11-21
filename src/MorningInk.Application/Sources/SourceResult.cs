namespace MorningInk.Application.Sources;

/// <summary>Outcome of fetching a source.</summary>
public abstract record SourceResult
{
    private SourceResult()
    {
    }

    /// <param name="Articles">Articles within the source's limits, newest first.</param>
    /// <param name="ETag">Cache validator to store on the source for the next fetch.</param>
    /// <param name="LastModified">Cache validator to store on the source for the next fetch.</param>
    public sealed record Success(IReadOnlyList<FetchedArticle> Articles, string? ETag, DateTimeOffset? LastModified) : SourceResult;

    /// <summary>The source has not changed since the last fetch, so there is nothing new.</summary>
    public sealed record NotModified : SourceResult;

    /// <param name="Message">Explanation for the issue's failure note; never contains credentials.</param>
    /// <param name="BlockedHost">Set when the home-network protection blocked it: the allowlist entry an admin could add.</param>
    public sealed record Failure(string Message, string? BlockedHost = null) : SourceResult;
}
