using System.Net;
using System.Net.Sockets;

namespace MorningInk.Infrastructure.Tests.Http;

/// <summary>A tiny HTTP server on the loopback interface; no real network is involved.</summary>
public sealed class LocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerContext, Task> _handle;

    public LocalServer(Func<HttpListenerContext, Task> handle)
    {
        _handle = handle;
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public int Port { get; }

    /// <summary>Requests received so far, with their headers.</summary>
    public List<(string Url, string? Authorization)> Requests { get; } = [];

    public Uri Url(string path, string host = "127.0.0.1") => new($"http://{host}:{Port}{path}");

    public void Dispose() => _listener.Close();

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            lock (Requests)
            {
                Requests.Add((context.Request.Url!.ToString(), context.Request.Headers["Authorization"])); // ! is safe: a received request always has a URL
            }

            await _handle(context);
            context.Response.Close();
        }
    }
}
