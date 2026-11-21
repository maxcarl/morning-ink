using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Tests.Persistence;

public sealed class DatabaseTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);

    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("morning-ink-").FullName;

    private string DatabasePath => Path.Combine(_dataDirectory, "morning-ink.db");

    private string BackupDirectory => Path.Combine(_dataDirectory, "backups");

    [Fact]
    public async Task Data_survives_a_restart()
    {
        await using (var services = await StartAsync())
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var user = new User { Name = "max" };
            var source = new Source { UserId = user.Id, Name = "heise", Url = "https://www.heise.de/rss/heise.rdf" };
            var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = "max@kindle.com", Region = "10115" };
            profile.Sections.Add(new Section { Name = "Tech", Position = 1, Sources = { source } });
            db.AddRange(user, source, profile);
            await db.SaveChangesAsync();
        }

        await using (var services = await StartAsync())
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var profile = await db.Profiles.Include(p => p.Sections).ThenInclude(s => s.Sources).SingleAsync();

            Assert.Equal("10115", profile.Region);
            var section = Assert.Single(profile.Sections);
            Assert.Equal("heise", Assert.Single(section.Sources).Name);
        }
    }

    [Fact]
    public async Task First_start_creates_the_database_without_a_backup()
    {
        await using var services = await StartAsync();

        Assert.True(File.Exists(DatabasePath));
        Assert.False(Directory.Exists(BackupDirectory));
    }

    [Fact]
    public async Task Migrating_an_existing_database_backs_it_up_first()
    {
        await CreateExistingDatabaseAsync();

        await using var services = await StartAsync();

        var backup = Assert.Single(Directory.GetFiles(BackupDirectory));
        Assert.Equal("morning-ink-20261121-060000.db", Path.GetFileName(backup));
        Assert.Equal("kept", await ReadMarkerAsync(backup));
    }

    [Fact]
    public async Task Start_without_pending_migrations_creates_no_backup()
    {
        await using (await StartAsync())
        {
        }

        await using var services = await StartAsync();

        Assert.False(Directory.Exists(BackupDirectory));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    private async Task<ServiceProvider> StartAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = _dataDirectory })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None);
        return services;
    }

    private async Task CreateExistingDatabaseAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE Marker (Value TEXT); INSERT INTO Marker VALUES ('kept');";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadMarkerAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Marker";
        return (string?)await command.ExecuteScalarAsync();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
