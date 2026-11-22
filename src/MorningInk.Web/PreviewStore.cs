using System.Collections.Concurrent;

namespace MorningInk.Web;

/// <summary>Each user's last preview, in memory for half an hour: a preview is looked at now, never kept.</summary>
public sealed class PreviewStore(TimeProvider time)
{
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, (byte[] File, DateTimeOffset Built)> _previews = new();

    public void Put(Guid userId, byte[] file) => _previews[userId] = (file, time.GetUtcNow());

    /// <summary>The user's preview file, or null when there is none or it is older than half an hour.</summary>
    public byte[]? Get(Guid userId)
    {
        if (!_previews.TryGetValue(userId, out var preview))
        {
            return null;
        }

        if (time.GetUtcNow() - preview.Built > KeepFor)
        {
            _previews.TryRemove(userId, out _);
            return null;
        }

        return preview.File;
    }
}
