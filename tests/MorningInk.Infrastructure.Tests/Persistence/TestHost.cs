using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Tests.Persistence;

/// <summary>A temporary data directory and the infrastructure services on top of it, started like the app starts.</summary>
public sealed class TestHost : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);

    public string DataDirectory { get; } = Directory.CreateTempSubdirectory("morning-ink-").FullName;

    public string DatabasePath => Path.Combine(DataDirectory, "morning-ink.db");

    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public string KeysDirectory => Path.Combine(DataDirectory, "keys");

    /// <summary>Builds fresh services, as after a restart, and applies migrations.</summary>
    /// <param name="settings">Extra configuration on top of the data directory.</param>
    /// <param name="configure">Extra registrations, e.g. application services.</param>
    public async Task<ServiceProvider> StartAsync(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null)
    {
        var values = new Dictionary<string, string?>(settings ?? []) { ["Storage:DataDirectory"] = DataDirectory };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var collection = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(Now))
            .AddInfrastructure(configuration);
        configure?.Invoke(collection);
        var services = collection.BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None);
        return services;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(DataDirectory, recursive: true);
    }
}
