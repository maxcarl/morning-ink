namespace MorningInk.Infrastructure.Weather;

/// <summary>Services for the weather tile. Configured in the "Weather" section; both can be self-hosted.</summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>Nominatim search endpoint (OpenStreetMap); asked only when a profile's region changes.</summary>
    public string GeocodingUrl { get; set; } = "https://nominatim.openstreetmap.org/search";

    /// <summary>Countries regions are looked up in when a profile or source has none, as ISO codes separated by commas; empty searches worldwide.</summary>
    public string CountryCodes { get; set; } = string.Empty;

    /// <summary>Language of place names when the country does not decide it.</summary>
    public string Language { get; set; } = "de";

    /// <summary>Open-Meteo forecast endpoint; free for non-commercial use, no key needed.</summary>
    public string ForecastUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";
}
