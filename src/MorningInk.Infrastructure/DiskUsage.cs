using Microsoft.Extensions.Options;

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
public sealed record StorageReport(IReadOnlyList<StorageUse> Areas, long? FreeBytes)
{
    public long TotalBytes => Areas.Sum(a => a.Bytes);
}

/// <summary>How much space Morning Ink takes in its data directory, for the admin's status page.</summary>
public sealed class DiskUsage(IOptions<StorageOptions> options)
{
    /// <remarks>Reads only sizes, no contents; even thousands of images take a moment at most.</remarks>
    public StorageReport Measure()
    {
        var storage = options.Value;
        var database = Path.GetFileName(storage.DatabasePath);
        return new StorageReport(
            [
                Measure(StorageArea.Database, storage.DataDirectoryPath, $"{database}*", SearchOption.TopDirectoryOnly),
                Measure(StorageArea.Issues, storage.IssueDirectory),
                Measure(StorageArea.Images, storage.ImageDirectory),
                Measure(StorageArea.Backups, storage.BackupDirectory),
            ],
            FreeSpace(storage.DataDirectoryPath));
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
