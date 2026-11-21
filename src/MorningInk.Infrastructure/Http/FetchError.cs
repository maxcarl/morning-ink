namespace MorningInk.Infrastructure.Http;

/// <summary>Expected reasons a fetch fails.</summary>
public enum FetchError
{
    /// <summary>Not an absolute http or https URL, contains credentials or is too long.</summary>
    InvalidUrl,

    /// <summary>Internal address or disallowed port, also when reached through a redirect.</summary>
    BlockedAddress,

    /// <summary>More redirects than <see cref="SafeHttpOptions.MaxRedirects"/>.</summary>
    TooManyRedirects,

    /// <summary>Body larger than <see cref="SafeHttpOptions.MaxResponseBytes"/>.</summary>
    TooLarge,

    /// <summary>No complete response within <see cref="SafeHttpOptions.Timeout"/>.</summary>
    Timeout,

    /// <summary>The server answered with an error status.</summary>
    HttpError,

    /// <summary>DNS failed or no connection could be made.</summary>
    Unreachable,
}
