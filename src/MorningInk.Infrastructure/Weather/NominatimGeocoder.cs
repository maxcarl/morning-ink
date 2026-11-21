using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningInk.Application.Weather;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Weather;

/// <summary>Resolves postal codes and town names with Nominatim (OpenStreetMap).</summary>
internal sealed partial class NominatimGeocoder(SafeHttpClient http, IOptions<WeatherOptions> options, ILogger<NominatimGeocoder> logger) : IGeocoder
{
    private const int MaxNameLength = 100;
    private const int MaxRegionLength = 100;

    public async Task<RegionLocation?> ResolveAsync(string region, string? country, CancellationToken cancellationToken)
    {
        region = region.Trim();
        if (region.Length is 0 or > MaxRegionLength)
        {
            return null;
        }

        var settings = options.Value;
        var countries = country?.ToLowerInvariant() ?? settings.CountryCodes;
        List<(string, string)> parameters =
        [
            PostalCode().IsMatch(region) ? ("postalcode", region) : ("q", region),

            // Place names in the country's language: they become search terms for its local news.
            ("accept-language", Countries.LanguageOf(country) ?? settings.Language),
            ("format", "jsonv2"),
            ("addressdetails", "1"),
            ("limit", "1"),
        ];
        if (countries.Length > 0)
        {
            parameters.Add(("countrycodes", countries));
        }

        if (await http.GetAsync(Urls.WithQuery(settings.GeocodingUrl, parameters), cancellationToken: cancellationToken) is not FetchResult.Success response)
        {
            LogUnavailable(logger);
            return null;
        }

        try
        {
            var place = JsonSerializer.Deserialize<Place[]>(response.Content)?.FirstOrDefault();
            return place is null ? null : ToLocation(RegionLocation.KeyFor(region, country), place);
        }
        catch (JsonException)
        {
            LogUnavailable(logger);
            return null;
        }
    }

    // The answer is untrusted: coordinates must be real coordinates, names must be short.
    private static RegionLocation? ToLocation(string key, Place place)
    {
        var address = place.Address;
        var name = address?.City ?? address?.Town ?? address?.Village ?? address?.Municipality;
        if (name is null || name.Length > MaxNameLength
            || !double.TryParse(place.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || latitude is < -90 or > 90
            || !double.TryParse(place.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) || longitude is < -180 or > 180)
        {
            return null;
        }

        var district = address?.County is { Length: <= MaxNameLength } county ? county : null;
        return new RegionLocation(key, name, district, latitude, longitude);
    }

    [GeneratedRegex(@"^\d{4,5}$")]
    private static partial Regex PostalCode();

    // The region is not logged: it tells where the reader lives.
    [LoggerMessage(Level = LogLevel.Information, Message = "Geocoding is unavailable; the issue comes without weather")]
    private static partial void LogUnavailable(ILogger logger);

    private sealed record Place(
        [property: JsonPropertyName("lat")] string? Latitude,
        [property: JsonPropertyName("lon")] string? Longitude,
        [property: JsonPropertyName("address")] Address? Address);

    private sealed record Address(
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("town")] string? Town,
        [property: JsonPropertyName("village")] string? Village,
        [property: JsonPropertyName("municipality")] string? Municipality,
        [property: JsonPropertyName("county")] string? County);
}
