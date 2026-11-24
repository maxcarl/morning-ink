using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MorningInk.Web.Tests;

/// <summary>A website with a linked RSS feed on the loopback interface; the articles are always fresh.</summary>
public sealed class FeedServer : IDisposable
{
    private readonly HttpListener _listener = new();

    public FeedServer()
    {
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public int Port { get; }

    public string SiteUrl => $"http://127.0.0.1:{Port}/";

    public string FeedUrl => $"http://127.0.0.1:{Port}/news.xml";

    /// <summary>A tag list in GitHub's format, with release 1.10.0 as the newest.</summary>
    public string TagsUrl => $"http://127.0.0.1:{Port}/tags";

    public void Dispose() => _listener.Close();

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

            var now = DateTimeOffset.UtcNow.ToString("R", CultureInfo.InvariantCulture);
            var path = context.Request.Url!.AbsolutePath; // ! is safe: a received request always has a URL
            var body = path == "/tags" ? """[{"name":"v1.10.0"},{"name":"v1.2.0"}]""" : path == "/news.xml"
                ? $"<rss version=\"2.0\"><channel><title>Beispiel</title><item><title>Erste Meldung</title><link>https://example.com/1</link><pubDate>{now}</pubDate></item><item><title>Zweite Meldung</title><link>https://example.com/2</link><pubDate>{now}</pubDate></item></channel></rss>"
                : "<html><head><title>Beispiel</title><link rel=\"alternate\" type=\"application/rss+xml\" title=\"Beispiel News\" href=\"/news.xml\"></head></html>";
            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(body));
            context.Response.Close();
        }
    }
}
