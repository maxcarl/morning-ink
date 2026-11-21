using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace MorningInk.Infrastructure.Http;

/// <summary>
/// Opens every TCP connection of the safe HTTP client. It resolves the host itself and connects only to
/// addresses it has checked, so a DNS answer cannot change between the check and the connection (DNS rebinding).
/// Because every redirect needs a new connection, redirect targets are checked here as well.
/// </summary>
internal sealed partial class SafeConnector(HostAllowlist allowlist, ILogger<SafeConnector> logger)
{
    private static readonly int[] DefaultPorts = [80, 443];

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        // IPv6 literals arrive in brackets, e.g. "[::1]".
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        var port = context.DnsEndPoint.Port;
        var allowlisted = allowlist.IsAllowed(host, port);

        if (!allowlisted && !DefaultPorts.Contains(port))
        {
            throw Blocked(host, port, $"port {port} is not allowed");
        }

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var permitted = allowlisted ? addresses : addresses.Where(a => !BlockedNetworks.IsBlocked(a)).ToArray();
        if (permitted.Length == 0)
        {
            throw Blocked(host, port, "it resolves only to internal addresses");
        }

        SocketException? lastError = null;
        foreach (var address in permitted)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
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

    private BlockedAddressException Blocked(string host, int port, string reason)
    {
        LogBlocked(logger, host, reason);
        var entry = DefaultPorts.Contains(port) ? host : $"{host}:{port}";
        return new BlockedAddressException($"Access to {host} is blocked: {reason}.", entry);
    }

    // Logs only the host: the full URL may carry an API key in its query.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked a request to {Host}: {Reason}")]
    private static partial void LogBlocked(ILogger logger, string host, string reason);
}
