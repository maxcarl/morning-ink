namespace MorningInk.Infrastructure.Http;

/// <summary>Rules every URL must pass before it is fetched, including redirect targets.</summary>
internal static class UrlPolicy
{
    private const int MaxUrlLength = 2048;

    /// <summary>Returns why <paramref name="uri"/> must not be fetched, or null if it may.</summary>
    public static string? Check(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return "The URL must be absolute.";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return "Only http and https URLs are allowed.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            // Credentials belong in a stored credential, not in a URL that ends up in logs and exports.
            return "URLs must not contain a username or password.";
        }

        return uri.OriginalString.Length > MaxUrlLength ? $"The URL is longer than {MaxUrlLength} characters." : null;
    }
}
