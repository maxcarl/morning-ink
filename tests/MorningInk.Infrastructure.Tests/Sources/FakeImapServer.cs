using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>
/// A minimal IMAP server on the loopback interface: enough of LOGIN, LIST, EXAMINE, UID SEARCH and UID FETCH to
/// serve what MailKit asks for when reading a folder. No TLS, so the tests allow its host in the home network.
/// </summary>
public sealed partial class FakeImapServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _user;
    private readonly string _password;
    private readonly Dictionary<string, List<MimeMessage>> _folders;

    public FakeImapServer(string user, string password, Dictionary<string, List<MimeMessage>> folders)
    {
        (_user, _password, _folders) = (user, password, folders);
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Every command received, without its tag, e.g. "EXAMINE Newsletter".</summary>
    public ConcurrentQueue<string> Commands { get; } = new();

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

            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        List<MimeMessage>? selected = null;
        await WriteAsync(stream, "* OK [CAPABILITY IMAP4rev1] fake ready\r\n");
        while (await reader.ReadLineAsync() is { } line)
        {
            var space = line.IndexOf(' ', StringComparison.Ordinal);
            var (tag, command) = (line[..space], line[(space + 1)..]);
            Commands.Enqueue(command);
            var verb = command.Split(' ')[0].ToUpperInvariant();
            var args = Arguments(command);
            switch (verb)
            {
                case "CAPABILITY":
                    await WriteAsync(stream, $"* CAPABILITY IMAP4rev1\r\n{tag} OK done\r\n");
                    break;
                case "LOGIN":
                    await WriteAsync(stream, args is [_, var u, var p] && u == _user && p == _password ? $"{tag} OK logged in\r\n" : $"{tag} NO [AUTHENTICATIONFAILED] wrong\r\n");
                    break;
                case "LIST":
                    var pattern = args[^1];
                    var answer = pattern.Length == 0
                        ? "* LIST (\\Noselect) \"/\" \"\"\r\n"
                        : string.Concat(_folders.Keys.Where(f => f == pattern || pattern == "*").Select(f => $"* LIST (\\HasNoChildren) \"/\" \"{f}\"\r\n"));
                    await WriteAsync(stream, $"{answer}{tag} OK done\r\n");
                    break;
                case "EXAMINE" or "SELECT":
                    selected = _folders.GetValueOrDefault(args[1]);
                    await WriteAsync(stream, selected is null
                        ? $"{tag} NO no such folder\r\n"
                        : $"* {selected.Count} EXISTS\r\n* 0 RECENT\r\n* OK [UIDVALIDITY 7] ok\r\n* OK [UIDNEXT {selected.Count + 1}] ok\r\n{tag} OK [{(verb == "EXAMINE" ? "READ-ONLY" : "READ-WRITE")}] done\r\n");
                    break;
                case "UID" when args[1].Equals("SEARCH", StringComparison.OrdinalIgnoreCase):
                    var from = FromCriterion().Match(command) is { Success: true } m ? m.Groups[1].Value : null;
                    var uids = (selected ?? []).Select((message, i) => (message, uid: i + 1))
                        .Where(x => from is null || x.message.From.Mailboxes.Any(a => a.Address.Contains(from, StringComparison.OrdinalIgnoreCase)))
                        .Select(x => x.uid);
                    await WriteAsync(stream, $"* SEARCH {string.Join(' ', uids)}\r\n{tag} OK done\r\n");
                    break;
                case "UID" when args[1].Equals("FETCH", StringComparison.OrdinalIgnoreCase):
                    await FetchAsync(stream, tag, selected ?? [], args[2], command);
                    break;
                case "LOGOUT":
                    await WriteAsync(stream, $"* BYE bye\r\n{tag} OK done\r\n");
                    return;
                default:
                    await WriteAsync(stream, $"{tag} OK done\r\n");
                    break;
            }
        }
    }

    private static async Task FetchAsync(NetworkStream stream, string tag, List<MimeMessage> messages, string set, string command)
    {
        foreach (var uid in Uids(set, messages.Count))
        {
            var bytes = Bytes(messages[uid - 1]);
            if (command.Contains("BODY.PEEK[]", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(stream, $"* {uid} FETCH (UID {uid} BODY[] {{{bytes.Length}}}\r\n");
                await stream.WriteAsync(bytes);
                await WriteAsync(stream, ")\r\n");
            }
            else
            {
                await WriteAsync(stream, $"* {uid} FETCH (UID {uid} RFC822.SIZE {bytes.Length})\r\n");
            }
        }

        await WriteAsync(stream, $"{tag} OK done\r\n");
    }

    // "1,3", "1:3", "2:*" or "3:1": IMAP ranges may run backwards.
    private static IEnumerable<int> Uids(string set, int count) =>
        set.Split(',').SelectMany(part =>
        {
            var range = part.Split(':').Select(n => n == "*" ? count : int.Parse(n, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var (low, high) = (range.Min(), Math.Min(range.Max(), count));
            return Enumerable.Range(low, Math.Max(0, high - low + 1));
        });

    private static byte[] Bytes(MimeMessage message)
    {
        using var buffer = new MemoryStream();
        message.WriteTo(buffer);
        return buffer.ToArray();
    }

    private static List<string> Arguments(string command) =>
        [.. Argument().Matches(command).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Value)];

    private static Task WriteAsync(NetworkStream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    [GeneratedRegex("\"([^\"]*)\"|[^ ]+")]
    private static partial Regex Argument();

    [GeneratedRegex("FROM \"?([^\" )]+)\"?", RegexOptions.IgnoreCase)]
    private static partial Regex FromCriterion();
}
