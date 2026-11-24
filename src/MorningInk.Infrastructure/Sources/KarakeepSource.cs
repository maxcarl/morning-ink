using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// The bookmarks of the user's Karakeep that are not archived: read later, in the next issue. Once an issue with them
/// is out, they are archived there, so the same bookmark never waits twice.
/// </summary>
internal sealed class KarakeepSource(KarakeepClient client, MorningInkDbContext db, TimeProvider time) : ISource
{
    public SourceKind Kind => SourceKind.Karakeep;

    public async Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        if (await KeyAsync(source, cancellationToken) is not var (instance, key))
        {
            return NoLogin;
        }

        var result = await client.UnarchivedAsync(instance, key, cancellationToken);
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

        if (await KeyAsync(source, cancellationToken) is not var (instance, key))
        {
            return new SourceAcknowledgement(0, NoLogin.Message);
        }

        var (archived, problem) = await client.ArchiveAsync(instance, key, articleIds, cancellationToken);
        return new SourceAcknowledgement(archived, problem);
    }

    private static readonly SourceResult.Failure NoLogin = new("This Karakeep source has no API key.", Reason: "KarakeepNoLogin");

    // Only the user's own key, and only for this source's own address; null when the key is gone.
    private async Task<(Uri Instance, string Key)?> KeyAsync(Source source, CancellationToken cancellationToken) =>
        await db.LoginOfAsync(source, cancellationToken) is { } credential && Uri.TryCreate(source.Url, UriKind.Absolute, out var instance)
            ? (instance, credential.Secret)
            : null;
}
