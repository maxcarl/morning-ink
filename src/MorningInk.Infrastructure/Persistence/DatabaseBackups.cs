using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>Why an uploaded backup was not accepted.</summary>
public enum RestoreProblem
{
    TooLarge,
    NotSqlite,

    /// <summary>SQLite's integrity check failed.</summary>
    Damaged,

    /// <summary>A SQLite file, but not a Morning Ink database.</summary>
    NotMorningInk,

    /// <summary>From a newer Morning Ink: this version does not know its schema.</summary>
    Newer,

    /// <summary>Holds triggers, which Morning Ink's own schema never has; they would run on every write.</summary>
    Unsafe,
}

/// <summary>
/// Database backups: a snapshot into the backup directory, and restoring an uploaded one. A restore is checked on
/// upload and applied at the next start, before migrations, when nothing has the database open.
/// </summary>
/// <remarks>
/// The keys in <see cref="StorageOptions.KeysDirectory"/> are never part of a backup. Restored on the same instance,
/// stored passwords stay readable; on another one they have to be entered again.
/// </remarks>
public sealed partial class DatabaseBackups(
    MorningInkDbContext db,
    IOptions<StorageOptions> options,
    TimeProvider time,
    ILogger<DatabaseBackups> logger)
{
    /// <summary>Largest upload accepted; a home instance's database is a few megabytes, years of history included.</summary>
    public const long MaxRestoreBytes = 512L * 1024 * 1024;

    private const string PendingFileName = "restore-pending.db";
    private const string UploadFileName = "restore-upload.tmp";
    private const string TimestampFormat = "yyyyMMdd-HHmmss-fff";
    private const string IntegrityOk = "ok";
    private const int CopyBufferBytes = 81920;

    // Every SQLite file starts with these 16 bytes.
    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    // SQLite keeps recent writes next to the database file until a checkpoint; a restore replaces all three.
    private static readonly string[] DatabaseFileSuffixes = [string.Empty, "-wal", "-shm"];

    private StorageOptions Storage => options.Value;

    private string PendingPath => Path.Combine(Storage.DataDirectoryPath, PendingFileName);

    /// <summary>Whether an uploaded backup waits for the next start.</summary>
    public bool RestorePending => File.Exists(PendingPath);

    /// <summary>Writes a consistent snapshot of the database to the backup directory; the cleanup keeps the newest few.</summary>
    /// <returns>The snapshot's path; its name carries the time, so name order is age order.</returns>
    public async Task<string> CreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Storage.BackupDirectory);
        var timestamp = time.GetUtcNow().ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var path = Path.Combine(Storage.BackupDirectory, $"morning-ink-{timestamp}.db");
        for (var n = 1; File.Exists(path); n++)
        {
            // Two in the same millisecond, e.g. before a restore and before the migration right after it.
            path = Path.Combine(Storage.BackupDirectory, $"morning-ink-{timestamp}-{n}.db");
        }

        // SQLite's online backup copies a consistent state, unlike copying the file next to a WAL journal.
        await using (var source = new SqliteConnection(ConnectionStrings.ForFile(Storage.DatabasePath)))
        await using (var target = new SqliteConnection(ConnectionStrings.ForFile(path)))
        {
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);
        }

        // Pooled connections would keep the snapshot open, and a download or the cleanup could not use it.
        SqliteConnection.ClearAllPools();
        LogBackedUp(logger, path);
        return path;
    }

    /// <summary>Checks an uploaded backup and keeps it for the next start, replacing one that waited before.</summary>
    /// <returns>Null when accepted, otherwise why not.</returns>
    public async Task<RestoreProblem?> StageRestoreAsync(Stream upload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Storage.DataDirectoryPath);

        // A fixed name, never the uploaded one.
        var temporary = Path.Combine(Storage.DataDirectoryPath, UploadFileName);
        try
        {
            if (!await CopyLimitedAsync(upload, temporary, cancellationToken))
            {
                return RestoreProblem.TooLarge;
            }

            if (await CheckAsync(temporary, cancellationToken) is { } problem)
            {
                LogRejected(logger, problem);
                return problem;
            }

            File.Move(temporary, PendingPath, overwrite: true);
            LogStaged(logger);
            return null;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Drops the backup waiting for the next start, if any.</summary>
    public void DiscardRestore() => File.Delete(PendingPath);

    /// <summary>
    /// At start, before migrations: replaces the database with the waiting backup, after a snapshot of the current one.
    /// Migrations then bring an older backup up to date.
    /// </summary>
    public async Task ApplyPendingRestoreAsync(CancellationToken cancellationToken)
    {
        if (!RestorePending)
        {
            return;
        }

        if (File.Exists(Storage.DatabasePath))
        {
            await CreateAsync(cancellationToken);
        }

        SqliteConnection.ClearAllPools();
        foreach (var suffix in DatabaseFileSuffixes)
        {
            File.Delete(Storage.DatabasePath + suffix);
        }

        File.Move(PendingPath, Storage.DatabasePath);
        LogRestored(logger);
    }

    private static async Task<bool> CopyLimitedAsync(Stream upload, string path, CancellationToken cancellationToken)
    {
        await using var file = File.Create(path);
        var buffer = new byte[CopyBufferBytes];
        long total = 0;
        int read;
        while ((read = await upload.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxRestoreBytes)
            {
                return false;
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return true;
    }

    private async Task<RestoreProblem?> CheckAsync(string path, CancellationToken cancellationToken)
    {
        var header = new byte[SqliteHeader.Length];
        await using (var file = File.OpenRead(path))
        {
            if (await file.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken) < header.Length
                || !header.AsSpan().SequenceEqual(SqliteHeader))
            {
                return RestoreProblem.NotSqlite;
            }
        }

        // Read-only and unpooled: the check changes nothing and leaves no handle on the file.
        var connection = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        await using var sqlite = new SqliteConnection(connection);
        try
        {
            await sqlite.OpenAsync(cancellationToken);
            if (await ScalarAsync<string>(sqlite, "PRAGMA integrity_check", cancellationToken) != IntegrityOk)
            {
                return RestoreProblem.Damaged;
            }

            if (await ScalarAsync<long>(sqlite, "SELECT count(*) FROM sqlite_master WHERE type = 'trigger'", cancellationToken) > 0)
            {
                return RestoreProblem.Unsafe;
            }

            if (await ScalarAsync<long>(sqlite, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'", cancellationToken) == 0)
            {
                return RestoreProblem.NotMorningInk;
            }

            var applied = new List<string>();
            await using (var command = sqlite.CreateCommand())
            {
                command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    applied.Add(reader.GetString(0));
                }
            }

            var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            return applied.Count == 0 ? RestoreProblem.NotMorningInk
                : applied.All(known.Contains) ? null
                : RestoreProblem.Newer;
        }
        catch (SqliteException)
        {
            // The header matched, but the rest is no database SQLite can read.
            return RestoreProblem.Damaged;
        }
    }

    // Fixed statements only; nothing from the upload goes into SQL text.
    private static async Task<T?> ScalarAsync<T>(SqliteConnection sqlite, string sql, CancellationToken cancellationToken)
    {
        await using var command = sqlite.CreateCommand();
        command.CommandText = sql;
        return (T?)await command.ExecuteScalarAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up the database to {BackupPath}")]
    private static partial void LogBackedUp(ILogger logger, string backupPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "An uploaded backup waits to be restored at the next start")]
    private static partial void LogStaged(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected an uploaded backup: {Problem}")]
    private static partial void LogRejected(ILogger logger, RestoreProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restored the database from an uploaded backup; the previous one is in the backup directory")]
    private static partial void LogRestored(ILogger logger);
}
