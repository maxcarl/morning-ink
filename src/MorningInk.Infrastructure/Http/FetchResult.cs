namespace MorningInk.Infrastructure.Http;

/// <summary>Outcome of a fetch: the body, or an expected error such as a blocked address.</summary>
public abstract record FetchResult
{
    private FetchResult()
    {
    }

    /// <param name="Content">Response body, at most <see cref="SafeHttpOptions.MaxResponseBytes"/>.</param>
    /// <param name="MediaType">Content type without parameters, e.g. "application/rss+xml".</param>
    /// <param name="FinalUri">Address after all redirects; relative links resolve against it.</param>
    /// <param name="Validators">ETag and Last-Modified of the response, for the next conditional request.</param>
    public sealed record Success(byte[] Content, string? MediaType, Uri FinalUri, CacheValidators Validators) : FetchResult;

    /// <summary>The server confirmed that the content is unchanged since the given validators.</summary>
    public sealed record NotModified : FetchResult;

    /// <param name="Error">What went wrong.</param>
    /// <param name="Message">Explanation safe to show to users; never contains credentials.</param>
    /// <param name="BlockedHost">For <see cref="FetchError.BlockedAddress"/>: the allowlist entry that would permit it.</param>
    public sealed record Failure(FetchError Error, string Message, string? BlockedHost = null) : FetchResult;
}
