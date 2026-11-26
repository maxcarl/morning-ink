using Microsoft.Extensions.Options;
using MorningInk.Application.Publishing;

namespace MorningInk.Infrastructure.Tests.Storage;

public sealed class DiskUsageTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("morning-ink-usage-").FullName;

    [Fact]
    public void Each_area_counts_its_own_files_and_the_database_its_log()
    {
        var storage = new StorageOptions { DataDirectory = _directory };
        File.WriteAllBytes(storage.DatabasePath, new byte[100]);
        File.WriteAllBytes(storage.DatabasePath + "-wal", new byte[20]);
        Directory.CreateDirectory(storage.IssueDirectory);
        File.WriteAllBytes(Path.Combine(storage.IssueDirectory, "a.epub"), new byte[300]);
        File.WriteAllBytes(Path.Combine(storage.IssueDirectory, "b.epub"), new byte[200]);

        var report = new DiskUsage(Options.Create(storage), new ProfileLocks()).Measure();

        Assert.Equal(
            [(StorageArea.Database, 120L, 2), (StorageArea.Issues, 500L, 2), (StorageArea.Images, 0L, 0), (StorageArea.Backups, 0L, 0)],
            report.Areas.Select(a => (a.Area, a.Bytes, a.Files)));
        Assert.Equal(620, report.TotalBytes);
        Assert.NotNull(report.FreeBytes);
    }

    [Fact]
    public void Image_cache_is_cleared_but_not_while_an_issue_is_being_built()
    {
        var storage = new StorageOptions { DataDirectory = _directory };
        Directory.CreateDirectory(storage.ImageDirectory);
        File.WriteAllBytes(Path.Combine(storage.ImageDirectory, "a.jpg"), new byte[10]);
        File.WriteAllBytes(Path.Combine(storage.ImageDirectory, "b.jpg"), new byte[10]);
        var locks = new ProfileLocks();
        var usage = new DiskUsage(Options.Create(storage), locks);

        Assert.True(locks.TryEnter(Guid.NewGuid(), out var building));
        using (building)
        {
            Assert.Null(usage.ClearImages()); // rendering reads them from disk
        }

        Assert.Equal(2, usage.ClearImages());
        Assert.Empty(Directory.EnumerateFiles(storage.ImageDirectory));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
