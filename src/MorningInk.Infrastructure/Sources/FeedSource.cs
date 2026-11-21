using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Sources;

/// <summary>RSS, RDF and Atom feeds, fetched through the safe HTTP client with conditional requests.</summary>
internal sealed class FeedSource(SafeHttpClient http, TimeProvider time) : ISource
{
    public SourceKind Kind => SourceKind.Rss;

    public async Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var url))
        {
            return new SourceResult.Failure("The feed address is not a valid URL.");
        }

        // ponytail: sources with a credential are fetched without it for now; sending it comes with private feeds.
        var fetched = await http.GetAsync(
            url,
            validators: new CacheValidators(source.ETag, source.LastModified),
            cancellationToken: cancellationToken);

        return fetched switch
        {
            FetchResult.NotModified => new SourceResult.NotModified(),
            FetchResult.Failure failure => new SourceResult.Failure(failure.Message),
            FetchResult.Success success => ApplyLimits(FeedParser.Parse(success.Content, success.FinalUri), source, success.Validators),
            _ => throw new InvalidOperationException($"Unknown fetch result {fetched.GetType().Name}."),
        };
    }

    private SourceResult ApplyLimits(SourceResult parsed, Source source, CacheValidators validators)
    {
        if (parsed is not SourceResult.Success success)
        {
            return parsed;
        }

        var oldest = time.GetUtcNow() - source.MaxAge;
        var articles = success.Articles
            .Where(a => a.PublishedAt is null || a.PublishedAt >= oldest)
            .OrderByDescending(a => a.PublishedAt ?? DateTimeOffset.MinValue)
            .Take(source.MaxArticles)
            .ToList();
        return new SourceResult.Success(articles, validators.ETag, validators.LastModified);
    }
}
