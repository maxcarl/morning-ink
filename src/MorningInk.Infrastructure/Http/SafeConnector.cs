using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace MorningInk.Infrastructure.Http;

/// <summary>
/// Opens every TCP connection of the safe HTTP client. It resolves the host itself and connects only to
/// addresses it has checked, so a DNS answer cannot change between the check and the connection (DNS rebinding).
/// Because every redirect needs a new connection, redirect targets are checked here as well.
/// IMAP connections for newsletters go through the same checks.
/// </summary>
public sealed partial class SafeConnector(HostAllowlist allowlist, ILogger<SafeConnector> logger)
{
    private static readonly int[] DefaultPorts = [80, 443];

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        new NetworkStream(await ConnectSocketAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, DefaultPorts, cancellationToken), ownsSocket: true);

    /// <summary>A connected socket after the same checks, for protocols other than HTTP, e.g. IMAP.</summary>
    /// <param name="openPorts">Ports any public host may be reached on; others need the host in the allowlist.</param>
    /// <exception cref="BlockedAddressException">The host is internal, or the port not open, and the host not allowed.</exception>
    public async Task<Socket> ConnectSocketAsync(string host, int port, IReadOnlyCollection<int> openPorts, CancellationToken cancellationToken)
    {
        // IPv6 literals arrive in brackets, e.g. "[::1]".
        host = host.Trim('[', ']');
        var allowlisted = allowlist.IsAllowed(host, port);

        if (!allowlisted && !openPorts.Contains(port))
        {
            throw Blocked(host, port, $"port {port} is not allowed", openPorts);
        }

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var permitted = allowlisted ? addresses : addresses.Where(a => !BlockedNetworks.IsBlocked(a)).ToArray();
        if (permitted.Length == 0)
        {
            throw Blocked(host, port, "it resolves only to internal addresses", openPorts);
        }

        SocketException? lastError = null;
        foreach (var address in permitted)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, port, cancellationToken);
                return socket;
            }
            catch (SocketException e)
            {
                // Try the next address, e.g. IPv4 when IPv6 is unreachable.
                socket.Dispose();
                lastError = e;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw lastError!; // ! is safe: permitted is not empty, so the loop ran and failed at least once
    }

    private BlockedAddressException Blocked(string host, int port, string reason, IReadOnlyCollection<int> openPorts)
    {
        LogBlocked(logger, host, reason);
        var entry = openPorts.Contains(port) ? host : $"{host}:{port}";
        return new BlockedAddressException($"Access to {host} is blocked: {reason}.", entry);
    }

    // Logs only the host: the full URL may carry an API key in its query.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked a request to {Host}: {Reason}")]
    private static partial void LogBlocked(ILogger logger, string host, string reason);
}
