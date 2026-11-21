using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// News about one place: the place is resolved to town and district once, then searched for. Several local sources
/// give several places, e.g. home and where the children live.
/// </summary>
internal sealed class LocalNewsSource(IGeocoder geocoder, INewsSearch search, TimeProvider time) : ISource
{
    public SourceKind Kind => SourceKind.Local;

    public async Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        var place = source.Place?.Trim();
        if (string.IsNullOrEmpty(place))
        {
            return new SourceResult.Failure("No place is set for this local source.");
        }

        // Geocoding only when the place changed; the result is saved with the source.
        if (source.Location?.Query != RegionLocation.KeyFor(place, source.Country))
        {
            source.Location = await geocoder.ResolveAsync(place, source.Country, cancellationToken);
        }

        if (source.Location is not { } location)
        {
            return new SourceResult.Failure($"The place \"{place}\" was not found.");
        }

        // The district catches the villages around a town; cities without one only search for themselves.
        string[] terms = location.District is { } district && district != location.Name ? [location.Name, district] : [location.Name];
        var result = await search.SearchAsync(new NewsQuery(terms, source.MaxAge, source.Country), cancellationToken);
        return result is SourceResult.Success success
            ? success with { Articles = SourceLimits.Apply(success.Articles, source, time.GetUtcNow()) }
            : result;
    }
}
