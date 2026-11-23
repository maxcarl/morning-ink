using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Tests.Persistence;

public sealed class DatabaseTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task Data_survives_a_restart()
    {
        await using (var services = await _host.StartAsync())
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var user = new User { Name = "alex" };
            var source = new Source { UserId = user.Id, Name = "heise", Url = "https://www.heise.de/rss/heise.rdf" };
            var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = "alex@kindle.com", Region = "10115" };
            profile.Sections.Add(new Section { Name = "Tech", Position = 1, Sources = { source } });
            db.AddRange(user, source, profile);
            await db.SaveChangesAsync();
        }

        await using (var services = await _host.StartAsync())
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
        await using var services = await _host.StartAsync();

        Assert.True(File.Exists(_host.DatabasePath));
        Assert.False(Directory.Exists(_host.BackupDirectory));
    }

    [Fact]
    public async Task Migrating_an_existing_database_backs_it_up_first()
    {
        await CreateExistingDatabaseAsync();

        await using var services = await _host.StartAsync();

        var backup = Assert.Single(Directory.GetFiles(_host.BackupDirectory));
        Assert.Equal("morning-ink-20261121-060000.db", Path.GetFileName(backup));
        Assert.Equal("kept", await ReadMarkerAsync(backup));
    }

    [Fact]
    public async Task Start_without_pending_migrations_creates_no_backup()
    {
        await using (await _host.StartAsync())
        {
        }

        await using var services = await _host.StartAsync();

        Assert.False(Directory.Exists(_host.BackupDirectory));
    }

    [Fact]
    public async Task Editions_from_before_section_kinds_get_their_weather_first_in_the_owners_language()
    {
        await using var services = await _host.StartAsync();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var migrator = db.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();

        // Back to the schema before section kinds, with an edition as it was then.
        await migrator.MigrateAsync("20261122190845_AddSendByEmail");
        var user = new User { Name = "alex", Language = "de" };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        var profileId = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "Profiles" ("Id", "UserId", "Title", "Recipient", "DeliveryTime", "TimeZoneId", "LeadTime", "Days", "NotifyChannel", "Interests", "SendByEmail")
            VALUES ({profileId}, {user.Id}, 'Morning Ink', '', '06:00:00', 'Europe/Berlin', '00:00:00', '[]', 'None', '[]', 1)
            """);
        await db.Database.ExecuteSqlAsync($"""INSERT INTO "Section" ("Id", "ProfileId", "Name", "Position") VALUES ({Guid.NewGuid()}, {profileId}, 'Politik', 1)""");

        await migrator.MigrateAsync();

        db.ChangeTracker.Clear();
        var sections = await db.Profiles.Where(p => p.Id == profileId).SelectMany(p => p.Sections).OrderBy(s => s.Position).ToListAsync();
        Assert.Equal([("Wetter", SectionKind.Weather, true), ("Politik", SectionKind.Articles, true)], sections.Select(s => (s.Name, s.Kind, s.Enabled)));
    }

    public void Dispose() => _host.Dispose();

    private async Task CreateExistingDatabaseAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_host.DatabasePath}");
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

}
