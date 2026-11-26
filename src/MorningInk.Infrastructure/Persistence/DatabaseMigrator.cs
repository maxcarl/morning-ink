using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>
/// At startup: restores a backup an admin uploaded, then applies pending migrations, backing up an existing
/// database before it changes.
/// </summary>
public sealed partial class DatabaseMigrator(
    MorningInkDbContext db,
    DatabaseBackups backups,
    IOptions<StorageOptions> options,
    ILogger<DatabaseMigrator> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var storage = options.Value;
        Directory.CreateDirectory(storage.DataDirectoryPath);
        await backups.ApplyPendingRestoreAsync(cancellationToken);

        // Checked before EF opens a connection, because opening one creates an empty database file.
        var databaseExists = File.Exists(storage.DatabasePath);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        if (databaseExists)
        {
            await backups.CreateAsync(cancellationToken);
        }

        await db.Database.MigrateAsync(cancellationToken);
        LogMigrated(logger, pending.Count, pending[^1]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} database migration(s), now at {Migration}")]
    private static partial void LogMigrated(ILogger logger, int count, string migration);
}
