using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>Stores seen articles as hashes of their normalized URL, one list per user.</summary>
internal sealed class SeenArticleStore(MorningInkDbContext db, TimeProvider time) : ISeenArticles
{
    public async Task<IReadOnlyList<FetchedArticle>> FilterNewAsync(
        Guid userId,
        IReadOnlyList<FetchedArticle> articles,
        CancellationToken cancellationToken)
    {
        var byHash = Distinct(articles);
        var seen = await SeenHashesAsync(userId, [.. byHash.Keys], cancellationToken);
        return byHash.Where(entry => !seen.Contains(entry.Key)).Select(entry => entry.Value).ToList();
    }

    public async Task MarkSeenAsync(Guid userId, IReadOnlyList<FetchedArticle> articles, CancellationToken cancellationToken)
    {
        var byHash = Distinct(articles);
        var seen = await SeenHashesAsync(userId, [.. byHash.Keys], cancellationToken);
        var now = time.GetUtcNow();
        db.SeenArticles.AddRange(byHash.Keys
            .Where(hash => !seen.Contains(hash))
            .Select(hash => new SeenArticle { UserId = userId, UrlHash = hash, SeenAt = now }));
        await db.SaveChangesAsync(cancellationToken);
    }

    // First article per key wins, keeping the order the sources delivered (newest first).
    private static Dictionary<string, FetchedArticle> Distinct(IReadOnlyList<FetchedArticle> articles)
    {
        var byHash = new Dictionary<string, FetchedArticle>(StringComparer.Ordinal);
        foreach (var article in articles)
        {
            byHash.TryAdd(Hash(article.Url), article);
        }

        return byHash;
    }

    private async Task<HashSet<string>> SeenHashesAsync(Guid userId, List<string> hashes, CancellationToken cancellationToken) =>
        (await db.SeenArticles
            .Where(s => s.UserId == userId && hashes.Contains(s.UrlHash))
            .Select(s => s.UrlHash)
            .ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.Ordinal);

    private static string Hash(Uri url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(UrlNormalizer.Normalize(url))));
}
