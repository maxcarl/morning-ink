using System.Globalization;
using System.Net;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Sources;

/// <summary>Where a newsletter source reads: the mail server, the login and the folder.</summary>
/// <param name="Sender">Only mails whose sender address contains this; null takes all.</param>
public sealed record NewsletterMailbox(string Host, int Port, string User, string Password, string Folder, string? Sender);

/// <summary>
/// Reads newsletters from an IMAP folder without changing the mailbox: the folder is opened read-only (EXAMINE) and
/// mails are fetched with BODY.PEEK, so none is marked read, moved or deleted. Connections pass the same home-network
/// check as every web request.
/// </summary>
public sealed class NewsletterClient(SafeConnector connector, HostAllowlist allowlist)
{
    /// <summary>The ports any public mail server may be reached on: IMAP with TLS, and IMAP with STARTTLS.</summary>
    public static readonly int[] OpenPorts = [ImapsPort, ImapPort];

    /// <summary>Larger mails are skipped unread; a newsletter with its images inline stays well below.</summary>
    public const int MaxMessageBytes = 2 * 1024 * 1024;

    private const int ImapsPort = 993;
    private const int ImapPort = 143;
    private const int MaxMessages = 50;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// TLS from the start on port 993, otherwise STARTTLS, which must succeed: the password never leaves the home
    /// network unencrypted. Only a server the admin allowed in the home network may answer without TLS.
    /// </summary>
    public static SecureSocketOptions Security(int port, bool homeNetwork) => port switch
    {
        ImapsPort => SecureSocketOptions.SslOnConnect,
        _ when homeNetwork => SecureSocketOptions.StartTlsWhenAvailable,
        _ => SecureSocketOptions.StartTls,
    };

    /// <summary>"host" or "host:port"; without a port, IMAP with TLS on 993.</summary>
    public static (string Host, int Port)? ParseServer(string text)
    {
        var trimmed = text.Trim();
        if (!Uri.TryCreate($"imap://{trimmed}", UriKind.Absolute, out var uri) || uri.Host.Length == 0 || uri.AbsolutePath != "/" || uri.UserInfo.Length > 0)
        {
            return null;
        }

        return (uri.Host, uri.IsDefaultPort || !trimmed.Contains(':', StringComparison.Ordinal) ? ImapsPort : uri.Port);
    }

    /// <summary>Signs in and opens the folder, to check a new source before it is saved; null when all is well.</summary>
    public async Task<SourceResult.Failure?> CheckAsync(NewsletterMailbox mailbox, CancellationToken cancellationToken)
    {
        var (_, failure) = await ReadAsync(mailbox, since: null, cancellationToken);
        return failure;
    }

    /// <summary>The folder's mails since the given time, newest first, as articles; with <paramref name="since"/> null, none are fetched.</summary>
    public async Task<(IReadOnlyList<FetchedArticle> Articles, SourceResult.Failure? Failure)> ReadAsync(NewsletterMailbox mailbox, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        using var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            var socket = await connector.ConnectSocketAsync(mailbox.Host, mailbox.Port, OpenPorts, cancellationToken);
            await client.ConnectAsync(socket, mailbox.Host, mailbox.Port, Security(mailbox.Port, allowlist.IsAllowed(mailbox.Host, mailbox.Port)), cancellationToken);
            await client.AuthenticateAsync(mailbox.User, mailbox.Password, cancellationToken);

            IMailFolder folder;
            try
            {
                folder = await client.GetFolderAsync(mailbox.Folder, cancellationToken);
                await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            }
            catch (Exception e) when (e is FolderNotFoundException or ImapCommandException)
            {
                return ([], new SourceResult.Failure($"The folder {mailbox.Folder} was not found.", Reason: "NewsletterFolder", Arguments: [mailbox.Folder]));
            }

            var articles = since is { } from ? await FetchAsync(folder, mailbox, from, cancellationToken) : [];
            await client.DisconnectAsync(quit: true, cancellationToken);
            return (articles, null);
        }
        catch (BlockedAddressException blocked)
        {
            return ([], new SourceResult.Failure(blocked.Message, blocked.AllowlistEntry, nameof(FetchError.BlockedAddress), [blocked.AllowlistEntry]));
        }
        catch (AuthenticationException)
        {
            return ([], new SourceResult.Failure($"{mailbox.Host} refused the sign-in.", Reason: "NewsletterLogin", Arguments: [mailbox.Host]));
        }
        catch (Exception e) when (e is SocketException or IOException or SslHandshakeException or ImapProtocolException or ImapCommandException or TimeoutException
            || (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return ([], new SourceResult.Failure($"{mailbox.Host} could not be reached.", Reason: nameof(FetchError.Unreachable), Arguments: [mailbox.Host]));
        }
    }

    private static async Task<IReadOnlyList<FetchedArticle>> FetchAsync(IMailFolder folder, NewsletterMailbox mailbox, DateTimeOffset since, CancellationToken cancellationToken)
    {
        // SINCE compares dates only; the source's age limit drops what is a little older.
        SearchQuery query = SearchQuery.DeliveredAfter(since.UtcDateTime.Date.AddDays(-1));
        if (!string.IsNullOrWhiteSpace(mailbox.Sender))
        {
            query = query.And(SearchQuery.FromContains(mailbox.Sender.Trim()));
        }

        var uids = (await folder.SearchAsync(query, cancellationToken)).OrderDescending().Take(MaxMessages).ToList();
        if (uids.Count == 0)
        {
            return [];
        }

        // Sizes first, so an oversized mail is never downloaded.
        var sizes = await folder.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Size, cancellationToken);
        var articles = new List<FetchedArticle>();
        foreach (var summary in sizes.Where(s => s.Size is <= MaxMessageBytes))
        {
            var message = await folder.GetMessageAsync(summary.UniqueId, cancellationToken);
            articles.Add(ToArticle(message, summary.UniqueId, folder.UidValidity, mailbox.Host));
        }

        return articles;
    }

    private static FetchedArticle ToArticle(MimeMessage message, UniqueId uid, uint validity, string host)
    {
        // The message id says which mail this is; "mid:" (RFC 2392) keeps it a valid address that is never fetched.
        var id = string.IsNullOrWhiteSpace(message.MessageId)
            ? string.Create(CultureInfo.InvariantCulture, $"{validity}.{uid.Id}@{host}")
            : message.MessageId;
        var from = message.From.Mailboxes.FirstOrDefault();
        return new FetchedArticle(
            Id: id,
            Title: string.IsNullOrWhiteSpace(message.Subject) ? from?.Address ?? host : message.Subject.Trim(),
            Url: new Uri("mid:" + Uri.EscapeDataString(id)),
            PublishedAt: message.Date == DateTimeOffset.MinValue ? null : message.Date,
            Author: string.IsNullOrWhiteSpace(from?.Name) ? from?.Address : from.Name,
            Summary: null,

            // HTML as it is, cleaned later like every article; plain text becomes encoded paragraphs.
            Content: message.HtmlBody ?? Paragraphs(message.TextBody));
    }

    private static string? Paragraphs(string? text) =>
        text is null
            ? null
            : string.Concat(text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => $"<p>{WebUtility.HtmlEncode(p).Replace("\n", "<br>", StringComparison.Ordinal)}</p>"));
}
