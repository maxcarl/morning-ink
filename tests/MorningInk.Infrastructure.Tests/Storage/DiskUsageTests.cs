using Microsoft.Extensions.Options;

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

        var report = new DiskUsage(Options.Create(storage)).Measure();

        Assert.Equal(
            [(StorageArea.Database, 120L, 2), (StorageArea.Issues, 500L, 2), (StorageArea.Images, 0L, 0), (StorageArea.Backups, 0L, 0)],
            report.Areas.Select(a => (a.Area, a.Bytes, a.Files)));
        Assert.Equal(620, report.TotalBytes);
        Assert.NotNull(report.FreeBytes);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
