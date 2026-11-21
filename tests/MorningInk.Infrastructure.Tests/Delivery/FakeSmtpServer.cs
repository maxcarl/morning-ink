using System.Net;
using System.Net.Sockets;
using System.Text;
using MimeKit;

namespace MorningInk.Infrastructure.Tests.Delivery;

/// <summary>
/// A minimal SMTP server on the loopback interface: enough of EHLO, AUTH PLAIN, MAIL, RCPT and DATA to receive
/// what MailKit sends. No TLS, so tests configure the client for an unencrypted connection explicitly.
/// </summary>
public sealed class FakeSmtpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly HashSet<string> _rejectedRecipients;

    public FakeSmtpServer(params string[] rejectedRecipients)
    {
        _rejectedRecipients = new HashSet<string>(rejectedRecipients, StringComparer.OrdinalIgnoreCase);
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int Connections { get; private set; }

    /// <summary>Decoded "user\0password" pairs received with AUTH PLAIN.</summary>
    public List<string> Logins { get; } = [];

    public List<MimeMessage> Messages { get; } = [];

    public void Dispose() => _listener.Stop();

    private async Task AcceptAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException)
            {
                return;
            }

            Connections++;
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 fake ESMTP");
        while (await reader.ReadLineAsync() is { } line)
        {
            var command = line.Split(' ', 2)[0].ToUpperInvariant();
            switch (command)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-fake\r\n250-AUTH PLAIN\r\n250 8BITMIME");
                    break;
                case "AUTH":
                    var parts = line.Split(' ');
                    var response = parts.Length > 2 ? parts[2] : await ReadChallengeAsync(reader, writer);
                    Logins.Add(Encoding.UTF8.GetString(Convert.FromBase64String(response)).TrimStart('\0'));
                    await writer.WriteLineAsync("235 ok");
                    break;
                case "MAIL":
                    await writer.WriteLineAsync("250 ok");
                    break;
                case "RCPT":
                    var address = line[(line.IndexOf('<', StringComparison.Ordinal) + 1)..line.IndexOf('>', StringComparison.Ordinal)];
                    await writer.WriteLineAsync(_rejectedRecipients.Contains(address) ? "550 no such user" : "250 ok");
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 go ahead");
                    Messages.Add(await ReadMessageAsync(reader));
                    await writer.WriteLineAsync("250 queued");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 bye");
                    return;
                default:
                    await writer.WriteLineAsync("250 ok");
                    break;
            }
        }
    }

    private static async Task<string> ReadChallengeAsync(StreamReader reader, StreamWriter writer)
    {
        await writer.WriteLineAsync("334 ");
        return await reader.ReadLineAsync() ?? string.Empty;
    }

    private static async Task<MimeMessage> ReadMessageAsync(StreamReader reader)
    {
        var raw = new StringBuilder();
        while (await reader.ReadLineAsync() is { } line && line != ".")
        {
            raw.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n");
        }

        return MimeMessage.Load(new MemoryStream(Encoding.ASCII.GetBytes(raw.ToString())));
    }
}
