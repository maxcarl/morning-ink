using System.Text.Json;
using Microsoft.Extensions.Options;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Updates;

/// <summary>
/// Looks for a release newer than the running one among the repository's tags ("v1.3.0"); pre-releases do not count.
/// GitHub is asked at most once a day, and any problem simply means no notice.
/// </summary>
public sealed class UpdateCheck(SafeHttpClient http, AppVersion running, IOptions<RepositoryOptions> options, UpdateCheck.Cache cache, TimeProvider time)
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    /// <summary>The newer release, e.g. "1.3.0"; null when there is none, the check is off or GitHub cannot be asked.</summary>
    public async Task<string?> NewerReleaseAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.CheckForUpdates || running.Release is not { } current)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (cache.Last is not { } last || now - last.At >= CheckEvery)
        {
            last = (now, await NewestAsync(cancellationToken));
            cache.Last = last;
        }

        return last.Newest is { } newest && newest > current ? newest.ToString() : null;
    }

    private async Task<Version?> NewestAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Tags is not { } url || await http.GetAsync(url, cancellationToken: cancellationToken) is not FetchResult.Success response)
        {
            return null;
        }

        try
        {
            // The answer is untrusted: only tag names that read as a plain version count.
            using var json = JsonDocument.Parse(response.Content);
            return json.RootElement.ValueKind == JsonValueKind.Array
                ? json.RootElement.EnumerateArray()
                    .Select(tag => tag.ValueKind == JsonValueKind.Object && tag.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? AppVersion.Parse(name.GetString() ?? string.Empty) : null)
                    .Max()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The last answer, kept for the whole app (singleton) so pages do not ask GitHub on every visit.</summary>
    public sealed class Cache
    {
        public (DateTimeOffset At, Version? Newest)? Last { get; set; }
    }
}
