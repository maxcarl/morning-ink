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
    public sealed record Success(byte[] Content, string? MediaType, Uri FinalUri) : FetchResult;

    /// <param name="Error">What went wrong.</param>
    /// <param name="Message">Explanation safe to show to users; never contains credentials.</param>
    public sealed record Failure(FetchError Error, string Message) : FetchResult;
}
