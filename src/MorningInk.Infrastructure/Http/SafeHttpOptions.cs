namespace MorningInk.Infrastructure.Http;

/// <summary>Limits for fetching untrusted URLs. Configured in the "Http" section.</summary>
public sealed class SafeHttpOptions
{
    public const string SectionName = "Http";

    /// <summary>Redirects followed per request; each target is checked again.</summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>Largest response body accepted, after decompression.</summary>
    public long MaxResponseBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Time for the whole request including redirects and reading the body.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Time to establish one TCP connection.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Hosts that may be reached although they are internal or use another port, e.g. a FreshRSS
    /// instance in the home network. An entry is "host" (any port) or "host:port".
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>Whether <paramref name="host"/> on <paramref name="port"/> is explicitly allowed.</summary>
    public bool IsAllowlisted(string host, int port) => AllowedHosts.Any(entry =>
        string.Equals(entry, host, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry, $"{host}:{port}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry, $"[{host}]:{port}", StringComparison.OrdinalIgnoreCase));
}
