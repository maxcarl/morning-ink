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
        if (source.Url is null || !Uri.TryCreate(source.Url, UriKind.Absolute, out var url))
        {
            return new SourceResult.Failure("The feed address is not a valid URL.", Reason: nameof(FetchError.InvalidUrl));
        }

        // ponytail: sources with a credential are fetched without it for now; sending it comes with private feeds.
        var fetched = await http.GetAsync(
            url,
            validators: new CacheValidators(source.ETag, source.LastModified),
            cancellationToken: cancellationToken);

        return fetched switch
        {
            FetchResult.NotModified => new SourceResult.NotModified(),
            FetchResult.Failure failure => failure.ToSourceFailure(),
            FetchResult.Success success => Limit(FeedParser.Parse(success.Content, success.FinalUri), source, success.Validators),
            _ => throw new InvalidOperationException($"Unknown fetch result {fetched.GetType().Name}."),
        };
    }

    private SourceResult Limit(SourceResult parsed, Source source, CacheValidators validators) => parsed is SourceResult.Success success
        ? new SourceResult.Success(SourceLimits.Apply(success.Articles, source, time.GetUtcNow()), validators.ETag, validators.LastModified)
        : parsed;
}
