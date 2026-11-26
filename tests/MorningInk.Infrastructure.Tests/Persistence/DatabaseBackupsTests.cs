using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Tests.Persistence;

public sealed class DatabaseBackupsTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task Restored_backup_replaces_the_database_at_the_next_start_and_the_replaced_one_is_kept()
    {
        string snapshot;
        await using (var services = await _host.StartAsync())
        {
            await AddUserAsync(services, "alex");
            snapshot = await UseAsync(services, backups => backups.CreateAsync(CancellationToken.None));
            await AddUserAsync(services, "erika");

            await using var upload = File.OpenRead(snapshot);
            Assert.Null(await UseAsync(services, backups => backups.StageRestoreAsync(upload, CancellationToken.None)));
            Assert.True(await UseAsync(services, backups => Task.FromResult(backups.RestorePending)));
        }

        await using (var services = await _host.StartAsync())
        {
            await using var scope = services.CreateAsyncScope();
            Assert.Equal(["alex"], await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Select(u => u.Name).ToListAsync());
            Assert.False(scope.ServiceProvider.GetRequiredService<DatabaseBackups>().RestorePending);
        }

        // The database it replaced, with erika, is in the backup directory next to the snapshot.
        var replaced = Assert.Single(Directory.GetFiles(_host.BackupDirectory), f => f != snapshot);
        Assert.Equal(2L, await ScalarAsync(replaced, "SELECT count(*) FROM Users"));
    }

    [Fact]
    public async Task Waiting_restore_can_be_discarded()
    {
        await using var services = await _host.StartAsync();
        var snapshot = await UseAsync(services, backups => backups.CreateAsync(CancellationToken.None));
        await using (var upload = File.OpenRead(snapshot))
        {
            await UseAsync(services, backups => backups.StageRestoreAsync(upload, CancellationToken.None));
        }

        Assert.False(await UseAsync(services, backups =>
        {
            backups.DiscardRestore();
            return Task.FromResult(backups.RestorePending);
        }));
    }

    [Fact]
    public async Task Upload_that_is_no_sqlite_file_is_rejected() =>
        Assert.Equal(RestoreProblem.NotSqlite, await StageAsync(new MemoryStream("not a database"u8.ToArray())));

    [Fact]
    public async Task Sqlite_file_of_another_program_is_rejected() =>
        Assert.Equal(RestoreProblem.NotMorningInk, await StageAsync(await DatabaseAsync("CREATE TABLE Notes (Text TEXT);")));

    [Fact]
    public async Task Backup_of_a_newer_version_is_rejected() =>
        Assert.Equal(RestoreProblem.Newer, await StageAsync(await DatabaseAsync("""
            CREATE TABLE __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);
            INSERT INTO __EFMigrationsHistory VALUES ('29991231000000_FromTheFuture', '99.0');
            """)));

    [Fact]
    public async Task Backup_with_a_trigger_is_rejected()
    {
        await using var services = await _host.StartAsync();
        var snapshot = await UseAsync(services, backups => backups.CreateAsync(CancellationToken.None));
        await ExecuteAsync(snapshot, "CREATE TRIGGER Sneaky AFTER INSERT ON Users BEGIN DELETE FROM Profiles; END;");

        await using var upload = File.OpenRead(snapshot);
        Assert.Equal(RestoreProblem.Unsafe, await UseAsync(services, backups => backups.StageRestoreAsync(upload, CancellationToken.None)));
        Assert.False(await UseAsync(services, backups => Task.FromResult(backups.RestorePending)));
    }

    public void Dispose() => _host.Dispose();

    private async Task<RestoreProblem?> StageAsync(Stream upload)
    {
        await using var services = await _host.StartAsync();
        await using (upload)
        {
            return await UseAsync(services, backups => backups.StageRestoreAsync(upload, CancellationToken.None));
        }
    }

    // A SQLite file outside the data directory, made with the given statements.
    private async Task<Stream> DatabaseAsync(string sql)
    {
        var path = Path.Combine(_host.DataDirectory, $"{Guid.NewGuid():N}.sqlite");
        await ExecuteAsync(path, sql);
        return new MemoryStream(await File.ReadAllBytesAsync(path));
    }

    private static async Task<T> UseAsync<T>(IServiceProvider services, Func<DatabaseBackups, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<DatabaseBackups>());
    }

    private static async Task AddUserAsync(IServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        db.DomainUsers.Add(new User { Name = name });
        await db.SaveChangesAsync();
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
