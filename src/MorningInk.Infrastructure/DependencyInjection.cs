using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MorningInk.Application.Delivery;
using MorningInk.Application.Processing;
using MorningInk.Application.Publishing;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Processing;
using MorningInk.Infrastructure.Rendering;
using MorningInk.Infrastructure.Scheduling;
using MorningInk.Infrastructure.Sources;
using Quartz;

namespace MorningInk.Infrastructure;

/// <summary>Registers the infrastructure layer, so the composition root needs one call.</summary>
public static class DependencyInjection
{
    // Fixed so keys stay valid when the app path or container changes.
    private const string DataProtectionApplicationName = "MorningInk";

    private const string UserAgent = "MorningInk/0.1 (+https://github.com/maxcarl/morning-ink)";

    /// <summary>Adds storage, Data Protection, the database, the safe HTTP client, sources, processors, renderers, delivery and scheduling.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // TryAdd, so tests can register a fixed clock first.
        services.TryAddSingleton(TimeProvider.System);

        AddStorage(services, configuration);
        AddPersistence(services);
        AddSafeHttp(services, configuration);
        AddSources(services);
        AddProcessors(services, configuration);
        services.AddTransient<IRenderer, EpubRenderer>();
        AddDelivery(services, configuration);
        AddScheduling(services, configuration);
        return services;
    }

    /// <summary>Brings the database up to date before the app starts serving requests.</summary>
    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(lifetime.ApplicationStopping);
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
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
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddDbContext<MorningInkDbContext>((provider, options) =>
            options
                .UseSqlite(ConnectionStrings.ForFile(provider.GetRequiredService<IOptions<StorageOptions>>().Value.DatabasePath))
                .ReplaceService<IModelCacheKeyFactory, DataProtectionModelCacheKeyFactory>());
        services.AddScoped<DatabaseMigrator>();
        services.AddScoped<ISeenArticles, SeenArticleStore>();
        services.AddScoped<IPublishingStore, PublishingStore>();
        services.AddSingleton<IIssueFiles, IssueFiles>();
        services.AddHealthChecks().AddDbContextCheck<MorningInkDbContext>();
    }

    // Every ISource is registered here; a new kind of source is one more line.
    private static void AddSources(IServiceCollection services)
    {
        services.AddTransient<ISource, FeedSource>();
    }

    // Processors run in registration order. Everything that brings in content runs before sanitizing; after it
    // only steps that remove markup or insert values they generated themselves (images). A test guards the order.
    private static void AddProcessors(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ImageOptions>()
            .Bind(configuration.GetSection(ImageOptions.SectionName))
            .Validate(o => o.MinDimension > 0 && o.MaxDimension >= o.MinDimension, "Images:MinDimension must be positive and not above Images:MaxDimension.")
            .Validate(o => o.Quality is >= 1 and <= 100, "Images:Quality must be between 1 and 100.")
            .ValidateOnStart();
        services.AddSingleton<ArticleSanitizer>();
        services.AddTransient<IProcessor, FullTextProcessor>();
        services.AddTransient<IProcessor, SanitizingProcessor>();
        services.AddTransient<IProcessor, ImageProcessor>();
    }

    // Not validated on start: mail can be set up later, and delivery reports a missing setup as a failure.
    private static void AddDelivery(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .Validate(o => o.Port is > 0 and <= 65535 && o.MaxMessageBytes > 0 && o.Timeout > TimeSpan.Zero, "Smtp:Port, Smtp:MaxMessageBytes and Smtp:Timeout must be positive.");
        services.AddTransient<IDelivery, MailDelivery>();
    }

    private static void AddScheduling(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Scheduling.SchedulingOptions>()
            .Bind(configuration.GetSection(Scheduling.SchedulingOptions.SectionName))
            .Validate(o => o.CatchUpWindow >= TimeSpan.Zero && o.FirstRetryDelay > TimeSpan.Zero && o.MaxDeliveryAttempts > 0, "Scheduling values must be positive.")
            .ValidateOnStart();

        services.AddQuartz(quartz => quartz.AddJobType<PublishIssueJob>());
        services.AddQuartzHostedService(quartz => quartz.WaitForJobsToComplete = true);
        services.AddSingleton<IssueScheduler>();

        // After Quartz' own hosted service, so the scheduler is running when the profiles are planned.
        services.AddHostedService<SchedulingStartup>();
    }

    private static void AddSafeHttp(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SafeHttpOptions>()
            .Bind(configuration.GetSection(SafeHttpOptions.SectionName))
            .Validate(o => o.MaxRedirects >= 0 && o.MaxResponseBytes > 0, "Http:MaxRedirects and Http:MaxResponseBytes must not be negative.")
            .Validate(o => o.Timeout > TimeSpan.Zero && o.ConnectTimeout > TimeSpan.Zero, "Http timeouts must be positive.")
            .ValidateOnStart();
        services.AddSingleton<SafeConnector>();

        services.AddHttpClient<SafeHttpClient>(client =>
            {
                // SafeHttpClient applies its own deadline across all redirects.
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
            {
                ConnectCallback = provider.GetRequiredService<SafeConnector>().ConnectAsync,
                ConnectTimeout = provider.GetRequiredService<IOptions<SafeHttpOptions>>().Value.ConnectTimeout,

                // Redirects are followed by SafeHttpClient, which checks every target and drops credentials.
                AllowAutoRedirect = false,

                // A proxy would make the connect callback connect to the proxy instead of the checked address.
                UseProxy = false,

                // Cookies are handled per credential later, never shared across hosts by a handler-wide container.
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
            });
    }
}
