using MorningInk.Application.Publishing;

namespace MorningInk.Web;

/// <summary>Registers the application layer. Lives here because Application has no DI package of its own.</summary>
public static class ApplicationServices
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Singleton, so the per-profile lock holds across all runs.
        services.AddSingleton<ProfileLocks>();
        services.AddScoped<IssueBuilder>();
        services.AddScoped<IssueRelease>();
        services.AddScoped<IssuePublisher>();
        return services;
    }
}
