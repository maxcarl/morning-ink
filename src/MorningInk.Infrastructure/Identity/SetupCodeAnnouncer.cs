using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MorningInk.Infrastructure.Identity;

/// <summary>At start, while no user exists: creates a setup code and writes it to the log.</summary>
internal sealed partial class SetupCodeAnnouncer(IServiceScopeFactory scopes, SetupCode code, ILogger<SetupCodeAnnouncer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (await scope.ServiceProvider.GetRequiredService<AdminSetup>().IsNeededAsync(cancellationToken))
        {
            LogCode(logger, code.Generate());
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // The only secret Morning Ink ever logs, on purpose: it is useless once the first admin exists.
    [LoggerMessage(Level = LogLevel.Warning, Message = "No user yet. Open /setup and enter this setup code: {Code}")]
    private static partial void LogCode(ILogger logger, string code);
}
