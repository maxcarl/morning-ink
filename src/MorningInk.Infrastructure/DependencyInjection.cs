using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
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
using MorningInk.Application.Weather;
using MorningInk.Application.Wikipedia;
using MorningInk.Infrastructure.Cleanup;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Identity;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Processing;
using MorningInk.Infrastructure.Rendering;
using MorningInk.Infrastructure.Scheduling;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Weather;
using MorningInk.Infrastructure.Wikipedia;
using Quartz;

namespace MorningInk.Infrastructure;

/// <summary>Registers the infrastructure layer, so the composition root needs one call.</summary>
public static class DependencyInjection
{
    // Fixed so keys stay valid when the app path or container changes.
    private const string DataProtectionApplicationName = "MorningInk";


    /// <summary>Adds storage, Data Protection, the database, the safe HTTP client, sources, processors, renderers, delivery and scheduling.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // TryAdd, so tests can register a fixed clock first.
        services.TryAddSingleton(TimeProvider.System);

        AddStorage(services, configuration);
        AddPersistence(services);
        AddSafeHttp(services, configuration);

        // The factory would log every request's address: a Telegram bot token is part of one, and searches name the
        // places a user reads about. Morning Ink logs hosts and reasons itself.
        services.ConfigureHttpClientDefaults(client => client.RemoveAllLoggers());
        AddSources(services, configuration);
        AddProcessors(services, configuration);
        services.AddTransient<IRenderer, EpubRenderer>();
        AddFeedback(services, configuration);
        AddDelivery(services, configuration);
        AddScheduling(services, configuration);
        AddIdentity(services);
        return services;
    }

    /// <summary>Brings the database up to date before the app starts serving requests.</summary>
    public static async Task MigrateDatabaseAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var lifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(lifetime.ApplicationStopping);
    }

    private static void AddFeedback(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WebOptions>().Bind(configuration.GetSection(WebOptions.SectionName));
        services.AddSingleton<Feedback.SignedFeedbackLinks>();
        services.AddSingleton<IFeedbackLinks>(provider => provider.GetRequiredService<Feedback.SignedFeedbackLinks>());
        services.AddScoped<Feedback.FeedbackRecorder>();
        services.AddScoped<Feedback.FeedbackStore>();
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
        services.AddScoped<DatabaseBackups>();
        services.AddScoped<ISeenArticles, SeenArticleStore>();
        services.AddScoped<IPublishingStore, PublishingStore>();
        services.AddSingleton<IIssueFiles, IssueFiles>();
        services.AddHealthChecks()
            .AddDbContextCheck<MorningInkDbContext>()
            .AddCheck<SchedulerHealthCheck>("scheduler")
            .AddCheck<FreeSpaceHealthCheck>("free-space");
    }

    // Every ISource is registered here; a new kind of source is one more line.
    private static void AddSources(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTransient<ISource, FeedSource>();
        services.AddTransient<ISource, LocalNewsSource>();
        services.AddTransient<ISource, FreshRssSource>();
        services.AddTransient<FreshRssClient>();
        services.AddTransient<ISource, KarakeepSource>();
        services.AddTransient<KarakeepClient>();
        services.AddTransient<ISource, NewsletterSource>();
        services.AddTransient<NewsletterClient>();
        services.AddTransient<ISource, HackerNewsSource>();
        services.AddTransient<ISource, RedditSource>();
        services.AddOptions<CommunityOptions>().Bind(configuration.GetSection(CommunityOptions.SectionName));
        services.AddTransient<FeedDiscovery>();
        services.AddScoped<SourceCatalog>();
        services.AddScoped<ConnectedSources>();
        services.AddScoped<Profiles.ProfileEditor>();
        services.AddScoped<Profiles.SectionEditor>();
        services.AddScoped<Profiles.Newsroom>();
        services.AddScoped<Profiles.IssueStatistics>();
        services.AddScoped<JobsOverview>();
        services.AddSingleton<DiskUsage>();
        services.AddScoped<Profiles.Onboarding>();
        services.AddScoped<Profiles.IssueArchive>();
        services.AddOptions<NewsSearchOptions>().Bind(configuration.GetSection(NewsSearchOptions.SectionName));
        // Bing or one's own SearXNG, chosen on every search from the settings (see NewsSearch).
        services.AddTransient<BingNewsSearch>();
        services.AddTransient<SearxngNewsSearch>();
        services.AddScoped<NewsSearchSettingsStore>();
        services.AddTransient<INewsSearch, NewsSearch>();

        services.AddOptions<WeatherOptions>().Bind(configuration.GetSection(WeatherOptions.SectionName));
        services.AddTransient<IGeocoder, NominatimGeocoder>();
        services.AddTransient<IWeatherForecast, OpenMeteoForecast>();

        services.AddOptions<WikipediaOptions>().Bind(configuration.GetSection(WikipediaOptions.SectionName));
        services.AddTransient<IOnThisDay, WikipediaOnThisDay>();
        services.AddTransient<IFeaturedArticle, WikipediaFeaturedArticle>();

        services.AddOptions<RepositoryOptions>().Bind(configuration.GetSection(RepositoryOptions.SectionName));
        services.AddSingleton(Updates.AppVersion.FromAssembly());
        services.AddSingleton<Updates.UpdateCheck.Cache>();
        services.AddTransient<Updates.UpdateCheck>();
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

        // Last: summarizes the cleaned full text. Scoped like every AI step, so one issue run stops asking once the AI has failed.
        services.AddScoped<IProcessor, Ai.AiSummaryProcessor>();
        services.AddScoped<IIssueOverview, Ai.AiOverviewWriter>();
        services.AddScoped<IHeadlineEditor, Ai.AiHeadlineEditor>();
        services.AddScoped<IInterestMatcher, Ai.AiInterestMatcher>();
        services.AddScoped<Ai.AiRequests>();
        services.AddScoped<Ai.AiSettingsStore>();
        services.AddSingleton<Ai.AiChat>();
        services.AddSingleton<Ai.AiUsage>();
    }

    // Not validated on start: mail can be set up later, and delivery reports a missing setup as a failure.
    private static void AddDelivery(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .Validate(o => o.Port is > 0 and <= 65535 && o.MaxMessageBytes > 0 && o.Timeout > TimeSpan.Zero, "Smtp:Port, Smtp:MaxMessageBytes and Smtp:Timeout must be positive.");
        services.AddTransient<MailDelivery>();
        services.AddTransient<IDelivery>(provider => provider.GetRequiredService<MailDelivery>());
        services.AddOptions<Notifications.NotificationOptions>().Bind(configuration.GetSection(Notifications.NotificationOptions.SectionName));
        services.AddTransient<MorningInk.Application.Notifications.INotifier, Notifications.Notifier>();
        services.AddScoped<MailSettingsStore>();
        services.AddTransient<TestMail>();

        // Which fields the configuration sets, so the UI can show them read-only.
        var section = configuration.GetSection(SmtpOptions.SectionName);
        services.AddSingleton(new MailSettingsLocks(section.GetChildren().Where(c => !string.IsNullOrEmpty(c.Value)).Select(c => c.Key).ToHashSet(StringComparer.OrdinalIgnoreCase)));
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(identity =>
            {
                // Length over complexity rules, as NIST recommends; long passphrases are welcome.
                identity.Password.RequiredLength = 12;
                identity.Password.RequireDigit = false;
                identity.Password.RequireLowercase = false;
                identity.Password.RequireUppercase = false;
                identity.Password.RequireNonAlphanumeric = false;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                identity.Lockout.AllowedForNewUsers = true;
                identity.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<MorningInkDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<RoleClaimsFactory>();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

        services.AddSingleton<SetupCode>();
        services.AddScoped<AdminSetup>();
        services.AddScoped<UserDirectory>();
        services.AddScoped<ReaderAccess>();
        services.AddSingleton<IPasswordHasher<Domain.User>, PasswordHasher<Domain.User>>();

        // Old sessions of a deleted user or one with a reset password end within a minute instead of the default 30.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));
        services.AddScoped<UserLanguages>();
        services.AddHostedService<SetupCodeAnnouncer>();
    }

    private static void AddScheduling(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<Scheduling.SchedulingOptions>()
            .Bind(configuration.GetSection(Scheduling.SchedulingOptions.SectionName))
            .Validate(o => o.CatchUpWindow >= TimeSpan.Zero && o.FirstRetryDelay > TimeSpan.Zero && o.MaxDeliveryAttempts > 0, "Scheduling values must be positive.")
            .ValidateOnStart();

        services.AddOptions<RetentionOptions>()
            .Bind(configuration.GetSection(RetentionOptions.SectionName))
            .Validate(o => o.IssueDays > 0 && o.SeenArticleDays > 0 && o.ImageDays > 0 && o.KeepBackups > 0, "Retention values must be positive.")
            .ValidateOnStart();
        services.AddScoped<CleanupService>();
        services.AddScoped<CleanupSchedule>();

        services.AddQuartz(quartz => quartz.AddJobType<PublishIssueJob>().AddJobType<CleanupJob>());
        services.AddQuartzHostedService(quartz => quartz.WaitForJobsToComplete = true);
        services.AddSingleton<IssueScheduler>();

        // After Quartz' own hosted service, so the scheduler is running when the profiles are planned.
        services.AddHostedService<SchedulingStartup>();
    }

    private static void AddSafeHttp(IServiceCollection services, IConfiguration configuration)
    {
        // FreshRSS and Karakeep: the home-network check on every connection, and no redirects, so the password, the
        // session and the API key only ever go to the address the user entered.
        foreach (var name in new[] { Sources.FreshRssClient.HttpClientName, Sources.KarakeepClient.HttpClientName })
        {
            services.AddHttpClient(name, client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.MaxResponseContentBufferSize = 20 * 1024 * 1024;
                })
                .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
                {
                    ConnectCallback = provider.GetRequiredService<SafeConnector>().ConnectAsync,
                    AllowAutoRedirect = false,
                    UseProxy = false,
                    UseCookies = false,
                });
        }

        // Notifications: the home-network check on every connection, short deadline, no redirects to follow.
        services.AddHttpClient(Notifications.Notifier.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
            {
                ConnectCallback = provider.GetRequiredService<SafeConnector>().ConnectAsync,
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
            });

        // The AI's own client: the same home-network check on every connection, but no response size limit or short
        // deadline, since a model on a CPU answers slowly. The OpenAI client applies its own network timeout.
        services.AddHttpClient(Ai.AiChat.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(provider => new SocketsHttpHandler
            {
                ConnectCallback = provider.GetRequiredService<SafeConnector>().ConnectAsync,
                UseProxy = false,
                UseCookies = false,
            });

        services.AddOptions<SafeHttpOptions>()
            .Bind(configuration.GetSection(SafeHttpOptions.SectionName))
            .Validate(o => o.MaxRedirects >= 0 && o.MaxResponseBytes > 0, "Http:MaxRedirects and Http:MaxResponseBytes must not be negative.")
            .Validate(o => o.Timeout > TimeSpan.Zero && o.ConnectTimeout > TimeSpan.Zero, "Http timeouts must be positive.")
            .ValidateOnStart();
        services.AddSingleton<HostAllowlist>();
        services.AddHostedService<HostAllowlistLoader>();
        services.AddSingleton<SafeConnector>();

        services.AddHttpClient<SafeHttpClient>((provider, client) =>
            {
                // SafeHttpClient applies its own deadline across all redirects.
                client.Timeout = Timeout.InfiniteTimeSpan;

                // Who is asking, so a site that minds can find the project: "MorningInk/1.2.0 (+https://github.com/…)".
                var repository = provider.GetRequiredService<IOptions<RepositoryOptions>>().Value.Url;
                client.DefaultRequestHeaders.UserAgent.TryParseAdd($"MorningInk/{provider.GetRequiredService<Updates.AppVersion>().Value} (+{repository})");
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
