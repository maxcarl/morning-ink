using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>Applies pending migrations at startup and backs up an existing database before it changes.</summary>
public sealed partial class DatabaseMigrator(
    MorningInkDbContext db,
    IOptions<StorageOptions> options,
    TimeProvider time,
    ILogger<DatabaseMigrator> logger)
{
    private const string BackupTimestampFormat = "yyyyMMdd-HHmmss";

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var storage = options.Value;
        Directory.CreateDirectory(storage.DataDirectoryPath);

        // Checked before EF opens a connection, because opening one creates an empty database file.
        var databaseExists = File.Exists(storage.DatabasePath);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        if (databaseExists)
        {
            await BackupAsync(storage, cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
        LogMigrated(logger, pending.Count, pending[^1]);
    }

    private async Task BackupAsync(StorageOptions storage, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(storage.BackupDirectory);
        var timestamp = time.GetUtcNow().ToString(BackupTimestampFormat, System.Globalization.CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(storage.BackupDirectory, $"morning-ink-{timestamp}.db");

        // SQLite's online backup copies a consistent state, unlike copying the file next to a WAL journal.
        await using var source = new SqliteConnection(ConnectionStrings.ForFile(storage.DatabasePath));
        await using var target = new SqliteConnection(ConnectionStrings.ForFile(backupPath));
        await source.OpenAsync(cancellationToken);
        await target.OpenAsync(cancellationToken);
        source.BackupDatabase(target);
        LogBackedUp(logger, backupPath);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up the database to {BackupPath} before migrating")]
    private static partial void LogBackedUp(ILogger logger, string backupPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} database migration(s), now at {Migration}")]
    private static partial void LogMigrated(ILogger logger, int count, string migration);
}
