using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningInk.Application.Weather;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Weather;

/// <summary>Three-day forecast from Open-Meteo, with morning, midday and evening of today.</summary>
internal sealed partial class OpenMeteoForecast(SafeHttpClient http, IOptions<WeatherOptions> options, ILogger<OpenMeteoForecast> logger) : IWeatherForecast
{
    private const int ForecastDays = 3;
    private const string Daily = "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max";
    private const string Hourly = "temperature_2m,precipitation_probability,weather_code";

    // Representative hours for the parts of the day shown for today.
    private static readonly (DayPart Part, int Hour)[] DayParts = [(DayPart.Morning, 8), (DayPart.Midday, 13), (DayPart.Evening, 18)];

    public async Task<WeatherForecast?> ForecastAsync(RegionLocation location, string timeZoneId, CancellationToken cancellationToken)
    {
        (string, string)[] parameters =
        [
            // Two decimals are about a kilometre: plenty for weather, and the exact place stays on the server.
            ("latitude", location.Latitude.ToString("F2", CultureInfo.InvariantCulture)),
            ("longitude", location.Longitude.ToString("F2", CultureInfo.InvariantCulture)),
            ("daily", Daily),
            ("hourly", Hourly),
            ("timezone", timeZoneId),
            ("forecast_days", ForecastDays.ToString(CultureInfo.InvariantCulture)),
        ];

        if (await http.GetAsync(Urls.WithQuery(options.Value.ForecastUrl, parameters), cancellationToken: cancellationToken) is not FetchResult.Success response)
        {
            LogUnavailable(logger);
            return null;
        }

        try
        {
            var forecast = JsonSerializer.Deserialize<Response>(response.Content);
            return forecast?.Daily is { } daily ? ToForecast(location.Name, daily, forecast.Hourly) : null;
        }
        catch (Exception e) when (e is JsonException or FormatException or IndexOutOfRangeException)
        {
            LogUnavailable(logger);
            return null;
        }
    }

    private static WeatherForecast? ToForecast(string place, DailyValues daily, HourlyValues? hourlyValues)
    {
        var days = new List<DayForecast>();
        for (var i = 0; i < daily.Time.Length; i++)
        {
            if (daily.WeatherCode[i] is { } code && daily.Min[i] is { } min && daily.Max[i] is { } max)
            {
                days.Add(new DayForecast(DateOnly.Parse(daily.Time[i], CultureInfo.InvariantCulture), code, min, max, daily.Precipitation[i]));
            }
        }

        if (days.Count == 0)
        {
            return null;
        }

        var today = new List<DayPartForecast>();
        if (hourlyValues is { } hourly)
        {
            foreach (var (part, hour) in DayParts)
            {
                var index = Array.IndexOf(hourly.Time, $"{days[0].Date:yyyy-MM-dd}T{hour:00}:00");
                if (index >= 0 && hourly.WeatherCode[index] is { } code && hourly.Temperature[index] is { } temperature)
                {
                    today.Add(new DayPartForecast(part, code, temperature, hourly.Precipitation[index]));
                }
            }
        }

        return new WeatherForecast(place, days, today);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The weather service is unavailable; the issue comes without weather")]
    private static partial void LogUnavailable(ILogger logger);

    private sealed record Response(
        [property: JsonPropertyName("daily")] DailyValues? Daily,
        [property: JsonPropertyName("hourly")] HourlyValues? Hourly);

    private sealed record DailyValues(
        [property: JsonPropertyName("time")] string[] Time,
        [property: JsonPropertyName("weather_code")] int?[] WeatherCode,
        [property: JsonPropertyName("temperature_2m_min")] double?[] Min,
        [property: JsonPropertyName("temperature_2m_max")] double?[] Max,
        [property: JsonPropertyName("precipitation_probability_max")] int?[] Precipitation);

    private sealed record HourlyValues(
        [property: JsonPropertyName("time")] string[] Time,
        [property: JsonPropertyName("weather_code")] int?[] WeatherCode,
        [property: JsonPropertyName("temperature_2m")] double?[] Temperature,
        [property: JsonPropertyName("precipitation_probability")] int?[] Precipitation);
}
