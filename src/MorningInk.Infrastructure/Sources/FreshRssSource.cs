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
        if (await SignInAsync(source, cancellationToken) is not ({ } api, { } session))
        {
            return NoLogin;
        }

        if (!session.Ok)
        {
            return session.Failure;
        }

        var result = await client.UnreadAsync(api, session.Body, source.Stream, cancellationToken);
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

        if (await SignInAsync(source, cancellationToken) is not ({ } api, { } session))
        {
            return new SourceAcknowledgement(0, NoLogin.Message);
        }

        if (!session.Ok)
        {
            return new SourceAcknowledgement(0, session.Failure.Message);
        }

        var problem = await client.MarkReadAsync(api, session.Body, articleIds, cancellationToken);
        return problem is null ? new SourceAcknowledgement(articleIds.Count, null) : new SourceAcknowledgement(0, problem);
    }

    private static readonly SourceResult.Failure NoLogin = new("This FreshRSS source has no login.", Reason: "FreshRssNoLogin");

    // Only the user's own login, and only for this source's own address; null when the login is gone.
    private async Task<(Uri Api, FreshRssClient.Answer Session)?> SignInAsync(Source source, CancellationToken cancellationToken)
    {
        var credential = await db.LoginOfAsync(source, cancellationToken);
        if (credential is null || !Uri.TryCreate(source.Url, UriKind.Absolute, out var api))
        {
            return null;
        }

        return (api, await client.SignInAsync(api, credential.Username ?? string.Empty, credential.Secret, cancellationToken));
    }
}
