namespace MorningInk.Infrastructure.Http;

/// <summary>
/// Thrown inside the connect callback to abort a connection; <see cref="SafeHttpClient"/> turns it into
/// <see cref="FetchError.BlockedAddress"/>, so it never escapes to callers.
/// </summary>
internal sealed class BlockedAddressException(string message) : Exception(message);
