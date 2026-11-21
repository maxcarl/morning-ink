using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure;

/// <summary>Registers the infrastructure layer, so the composition root needs one call.</summary>
public static class DependencyInjection
{
    // Fixed so keys stay valid when the app path or container changes.
    private const string DataProtectionApplicationName = "MorningInk";

    /// <summary>Adds storage options, Data Protection, the database and its health check.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.DataDirectory), "Storage:DataDirectory must be set.")
            .ValidateOnStart();

        // Needed while registering, before options can be resolved; same section as bound above.
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(storage.KeysDirectory));

        // TryAdd, so tests can register a fixed clock first.
        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<MorningInkDbContext>((provider, options) =>
            options
                .UseSqlite(ConnectionStrings.ForFile(provider.GetRequiredService<IOptions<StorageOptions>>().Value.DatabasePath))
                .ReplaceService<IModelCacheKeyFactory, DataProtectionModelCacheKeyFactory>());
        services.AddScoped<DatabaseMigrator>();
        services.AddHealthChecks().AddDbContextCheck<MorningInkDbContext>();

        return services;
    }

    /// <summary>Brings the database up to date before the app starts serving requests.</summary>
    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(lifetime.ApplicationStopping);
    }
}
