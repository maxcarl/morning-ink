namespace MorningInk.Infrastructure.Http;

/// <summary>
/// Thrown inside the connect callback to abort a connection; <see cref="SafeHttpClient"/> turns it into
/// <see cref="FetchError.BlockedAddress"/>, so it never escapes to callers.
/// </summary>
internal sealed class BlockedAddressException(string message, string allowlistEntry) : Exception(message)
{
    /// <summary>What an admin would add to the allowlist to permit it, e.g. "freshrss.lan" or "192.168.1.20:8080".</summary>
    public string AllowlistEntry { get; } = allowlistEntry;
}
