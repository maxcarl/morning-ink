using Microsoft.Extensions.Options;
using MorningInk.Application.Publishing;

namespace MorningInk.Infrastructure;

/// <summary>A part of the data directory.</summary>
public enum StorageArea
{
    /// <summary>The SQLite file with its write-ahead log.</summary>
    Database,

    Issues,
    Images,
    Backups,
}

/// <param name="Bytes">Size of all its files.</param>
public sealed record StorageUse(StorageArea Area, long Bytes, int Files);

/// <param name="Areas">What each part of the data directory takes.</param>
/// <param name="FreeBytes">Free space on the volume of the data directory; null where the system does not tell.</param>
/// <param name="TooLarge">The data directory takes more than <see cref="StorageOptions.WarnAboveMegabytes"/>.</param>
/// <param name="LowOnSpace">Less than <see cref="StorageOptions.MinFreeMegabytes"/> are free.</param>
public sealed record StorageReport(IReadOnlyList<StorageUse> Areas, long? FreeBytes, bool TooLarge, bool LowOnSpace)
{
    public long TotalBytes => Areas.Sum(a => a.Bytes);
}

/// <summary>How much space Morning Ink takes in its data directory, and clearing the image cache; for the admin's status page.</summary>
public sealed class DiskUsage(IOptions<StorageOptions> options, ProfileLocks locks)
{
    private const long BytesPerMegabyte = 1024 * 1024;

    /// <remarks>Reads only sizes, no contents; even thousands of images take a moment at most.</remarks>
    public StorageReport Measure()
    {
        var storage = options.Value;
        var database = Path.GetFileName(storage.DatabasePath);
        List<StorageUse> areas =
        [
            Measure(StorageArea.Database, storage.DataDirectoryPath, $"{database}*", SearchOption.TopDirectoryOnly),
            Measure(StorageArea.Issues, storage.IssueDirectory),
            Measure(StorageArea.Images, storage.ImageDirectory),
            Measure(StorageArea.Backups, storage.BackupDirectory),
        ];
        var free = FreeSpace(storage.DataDirectoryPath);
        var tooLarge = storage.WarnAboveMegabytes is { } limit && areas.Sum(a => a.Bytes) > limit * BytesPerMegabyte;
        return new StorageReport(areas, free, tooLarge, IsLow(free));
    }

    /// <summary>Whether the volume is short of <see cref="StorageOptions.MinFreeMegabytes"/>; cheap, for <c>/health</c>.</summary>
    /// <remarks>False where the system does not tell the free space: no warning without a measurement.</remarks>
    public bool LowOnSpace() => IsLow(FreeSpace(options.Value.DataDirectoryPath));

    private bool IsLow(long? free) => free < options.Value.MinFreeMegabytes * BytesPerMegabyte;

    /// <summary>
    /// Deletes all converted images ahead of the nightly cleanup. They are only needed until an issue is rendered and
    /// live in its EPUB after that, so archived issues keep theirs.
    /// </summary>
    /// <returns>The number deleted; null while an issue is being built, since rendering reads the images from disk.</returns>
    /// <remarks>ponytail: a build starting during the deletion just goes without the images deleted under it.</remarks>
    public int? ClearImages()
    {
        var directory = options.Value.ImageDirectory;
        if (locks.AnyHeld)
        {
            return null;
        }

        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
        {
            try
            {
                file.Delete();
                deleted++;
            }
            catch (IOException)
            {
                // Still open somewhere: the nightly cleanup gets it.
            }
        }

        return deleted;
    }

    private static StorageUse Measure(StorageArea area, string directory, string pattern = "*", SearchOption depth = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(directory))
        {
            return new StorageUse(area, 0, 0);
        }

        var files = new DirectoryInfo(directory).EnumerateFiles(pattern, depth).ToList();
        return new StorageUse(area, files.Sum(f => f.Length), files.Count);
    }

    private static long? FreeSpace(string directory)
    {
        try
        {
            return new DriveInfo(directory).AvailableFreeSpace;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
