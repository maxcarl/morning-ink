using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// Newsletters from a folder of the user's mailbox. Read only: which mails were already in an issue is known from
/// the seen articles, so the mailbox needs no flag and stays as the user left it.
/// </summary>
internal sealed class NewsletterSource(NewsletterClient client, MorningInkDbContext db, TimeProvider time) : ISource
{
    public SourceKind Kind => SourceKind.Newsletter;

    public async Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        if (await MailboxAsync(source, cancellationToken) is not { } mailbox)
        {
            return new SourceResult.Failure("This newsletter source has no login.", Reason: "NewsletterNoLogin");
        }

        var now = time.GetUtcNow();
        var (articles, failure) = await client.ReadAsync(mailbox, now - source.MaxAge, cancellationToken);
        return failure is not null ? failure : new SourceResult.Success(SourceLimits.Apply(articles, source, now), ETag: null, LastModified: null);
    }

    /// <summary>The source's mailbox with the user's own login; null when it is missing.</summary>
    internal async Task<NewsletterMailbox?> MailboxAsync(Source source, CancellationToken cancellationToken)
    {
        var credential = await db.LoginOfAsync(source, cancellationToken);
        return credential is null || !Uri.TryCreate(source.Url, UriKind.Absolute, out var server)
            ? null
            : new NewsletterMailbox(server.Host, server.Port, credential.Username ?? string.Empty, credential.Secret, source.Stream ?? "INBOX", source.Sender);
    }
}
