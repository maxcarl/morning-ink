using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.DataDirectory), "Storage:DataDirectory must be set.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<MorningInkDbContext>((provider, options) =>
            options.UseSqlite(ConnectionStrings.ForFile(provider.GetRequiredService<IOptions<StorageOptions>>().Value.DatabasePath)));
        services.AddScoped<DatabaseMigrator>();
        services.AddHealthChecks().AddDbContextCheck<MorningInkDbContext>();

        return services;
    }

    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(lifetime.ApplicationStopping);
    }
}
