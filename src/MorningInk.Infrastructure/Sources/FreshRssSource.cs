using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// Unread articles of one category of the user's FreshRSS; once an issue with them is out, they are marked read there,
/// so FreshRSS and the newspaper never show the same article as new twice.
/// </summary>
internal sealed class FreshRssSource(FreshRssClient client, MorningInkDbContext db, TimeProvider time) : ISource
{
    public SourceKind Kind => SourceKind.FreshRss;

    public async Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        var (api, session, failure) = await SignInAsync(source, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        var result = await client.UnreadAsync(api!, session!, source.Stream, cancellationToken); // ! is safe: no failure means both are set
        return result is SourceResult.Success success
            ? success with { Articles = SourceLimits.Apply(success.Articles, source, time.GetUtcNow()) }
            : result;
    }

    public async Task<SourceAcknowledgement> AcknowledgeAsync(Source source, IReadOnlyList<string> articleIds, CancellationToken cancellationToken)
    {
        if (!source.MarkReadAfterDelivery || articleIds.Count == 0)
        {
            return SourceAcknowledgement.Nothing;
        }

        var (api, session, failure) = await SignInAsync(source, cancellationToken);
        if (failure is not null)
        {
            return new SourceAcknowledgement(0, failure.Message);
        }

        var problem = await client.MarkReadAsync(api!, session!, articleIds, cancellationToken); // ! is safe: no failure means both are set
        return problem is null ? new SourceAcknowledgement(articleIds.Count, null) : new SourceAcknowledgement(0, problem);
    }

    private async Task<(Uri? Api, string? Session, SourceResult.Failure? Failure)> SignInAsync(Source source, CancellationToken cancellationToken)
    {
        // Only the user's own login, and only for this source's own address.
        var credential = await db.LoginOfAsync(source, cancellationToken);
        if (credential is null || !Uri.TryCreate(source.Url, UriKind.Absolute, out var api))
        {
            return (null, null, new SourceResult.Failure("This FreshRSS source has no login.", Reason: "FreshRssNoLogin"));
        }

        var (session, failure) = await client.SignInAsync(api, credential.Username ?? string.Empty, credential.Secret, cancellationToken);
        return (api, session, failure);
    }
}
