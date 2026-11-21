using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Sources;

/// <summary>The age and count limits every source applies to what it delivers.</summary>
internal static class SourceLimits
{
    /// <summary>Drops articles older than <see cref="Source.MaxAge"/> (undated ones stay) and keeps the newest <see cref="Source.MaxArticles"/>.</summary>
    public static List<FetchedArticle> Apply(IEnumerable<FetchedArticle> articles, Source source, DateTimeOffset now)
    {
        var oldest = now - source.MaxAge;
        return articles
            .Where(a => a.PublishedAt is null || a.PublishedAt >= oldest)
            .OrderByDescending(a => a.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(source.MaxArticles)
            .ToList();
    }
}
