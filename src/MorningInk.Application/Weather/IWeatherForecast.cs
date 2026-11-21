using MorningInk.Domain;

namespace MorningInk.Application.Weather;

/// <summary>Weather forecast for a place.</summary>
public interface IWeatherForecast
{
    /// <summary>Forecast for today and the next days in the given time zone, or null if unavailable.</summary>
    Task<WeatherForecast?> ForecastAsync(RegionLocation location, string timeZoneId, CancellationToken cancellationToken);
}

/// <param name="Place">Name shown with the forecast, e.g. "Berlin".</param>
/// <param name="Days">Today first, then the following days.</param>
/// <param name="Today">Morning, midday and evening of today; may be empty.</param>
public sealed record WeatherForecast(string Place, IReadOnlyList<DayForecast> Days, IReadOnlyList<DayPartForecast> Today);

/// <param name="WeatherCode">WMO weather interpretation code, e.g. 61 for slight rain.</param>
/// <param name="PrecipitationProbability">Highest chance of precipitation that day in percent, if known.</param>
public sealed record DayForecast(DateOnly Date, int WeatherCode, double MinTemperature, double MaxTemperature, int? PrecipitationProbability);

public sealed record DayPartForecast(DayPart Part, int WeatherCode, double Temperature, int? PrecipitationProbability);

/// <summary>Parts of the day shown for today.</summary>
public enum DayPart
{
    Morning,
    Midday,
    Evening,
}
