using Microsoft.Extensions.Hosting;

namespace MorningInk.Infrastructure.Http;

/// <summary>Loads the stored allowlist at start; runs after migrations, which happen before the host starts.</summary>
internal sealed class HostAllowlistLoader(HostAllowlist allowlist) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => allowlist.LoadAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
