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

    /// <param name="Message">Explanation in English, for logs and as a fallback; never contains credentials.</param>
    /// <param name="BlockedHost">Set when the home-network protection blocked it: the allowlist entry an admin could add.</param>
    /// <param name="Reason">A stable code such as "Unreachable", which the UI turns into words in the user's language.</param>
    /// <param name="Arguments">The values the words need, e.g. the host.</param>
    public sealed record Failure(string Message, string? BlockedHost = null, string? Reason = null, IReadOnlyList<string>? Arguments = null) : SourceResult
    {
        public IReadOnlyList<string> Arguments { get; init; } = Arguments ?? [];
    }
}
