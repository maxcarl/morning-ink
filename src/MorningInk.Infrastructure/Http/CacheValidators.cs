namespace MorningInk.Infrastructure.Http;

/// <summary>Values from an earlier response that let the server answer "not modified" instead of the full body.</summary>
/// <param name="ETag">Sent as If-None-Match.</param>
/// <param name="LastModified">Sent as If-Modified-Since.</param>
public sealed record CacheValidators(string? ETag, DateTimeOffset? LastModified);
