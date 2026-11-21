using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Weather;
using MorningInk.Domain;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Weather;

/// <summary>Geocoding (Nominatim) and forecast (Open-Meteo) against saved real responses on a local server.</summary>
public sealed class WeatherTests : IDisposable
{
    private static readonly RegionLocation Berlin = new("10115", "Berlin", null, 52.5321914, 13.3845571);

    private readonly LocalServer _server = new(async context =>
    {
        var query = context.Request.QueryString;
        var file = context.Request.Url!.AbsolutePath switch // ! is safe: a received request always has a URL
        {
            "/search" when query["postalcode"] == "10115" => "nominatim-10115.json",
            "/search" when query["q"] == "Aachen" => "nominatim-aachen.json",
            "/search" => "nominatim-unknown.json",
            "/v1/forecast" => "forecast-berlin.json",
            _ => null,
        };
        if (file is null || query["fail"] is not null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Weather", file)));
    });

    [Fact]
    public async Task Postal_code_resolves_to_town_and_coordinates()
    {
        var location = await Geocoder().ResolveAsync("10115", "DE", CancellationToken.None);

        Assert.Equal(new RegionLocation("10115 (DE)", "Berlin", null, 52.5321914, 13.3845571), location);
        var url = _server.Requests.Single().Url;
        Assert.Contains("postalcode=10115", url, StringComparison.Ordinal);
        Assert.Contains("countrycodes=de", url, StringComparison.Ordinal);
        Assert.Contains("accept-language=de", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Town_resolves_with_its_district()
    {
        var location = await Geocoder().ResolveAsync("Aachen", "DE", CancellationToken.None);

        Assert.Equal("Aachen", location?.Name);
        Assert.Equal("Städteregion Aachen", location?.District);
        Assert.Contains("q=Aachen", _server.Requests.Single().Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Xyzzyqwerty")]
    [InlineData("")]
    public async Task Unknown_region_resolves_to_nothing(string region) =>
        Assert.Null(await Geocoder().ResolveAsync(region, "DE", CancellationToken.None));

    [Fact]
    public async Task The_same_postal_code_is_looked_up_in_the_chosen_country_or_worldwide()
    {
        await Geocoder().ResolveAsync("10115", "US", CancellationToken.None);
        await Geocoder().ResolveAsync("10115", null, CancellationToken.None);

        var (us, worldwide) = (_server.Requests[0].Url, _server.Requests[1].Url);
        Assert.Contains("countrycodes=us", us, StringComparison.Ordinal);
        Assert.Contains("accept-language=en", us, StringComparison.Ordinal);
        Assert.DoesNotContain("countrycodes", worldwide, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forecast_has_three_days_and_the_parts_of_today()
    {
        var forecast = await Forecast().ForecastAsync(Berlin, "Europe/Berlin", CancellationToken.None);

        Assert.NotNull(forecast);
        Assert.Equal("Berlin", forecast.Place);
        Assert.Equal(new DayForecast(new DateOnly(2026, 11, 21), 80, 0.5, 6.7, 45), forecast.Days[0]);
        Assert.Equal(3, forecast.Days.Count);
        Assert.Equal(
            [new DayPartForecast(DayPart.Morning, 3, 1.3, 0), new DayPartForecast(DayPart.Midday, 3, 6.4, 20), new DayPartForecast(DayPart.Evening, 3, 6.1, 40)],
            forecast.Today);
    }

    [Fact]
    public async Task Forecast_request_uses_rounded_invariant_coordinates_and_the_time_zone()
    {
        await Forecast().ForecastAsync(Berlin, "Europe/Berlin", CancellationToken.None);

        var url = _server.Requests.Single().Url;
        Assert.Contains("latitude=52.53&", url, StringComparison.Ordinal);
        Assert.Contains("longitude=13.38&", url, StringComparison.Ordinal);
        Assert.Contains("timezone=Europe%2FBerlin", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unavailable_services_give_nothing()
    {
        Assert.Null(await Geocoder(fail: true).ResolveAsync("10115", "DE", CancellationToken.None));
        Assert.Null(await Forecast(fail: true).ForecastAsync(Berlin, "Europe/Berlin", CancellationToken.None));
    }

    public void Dispose() => _server.Dispose();

    private IGeocoder Geocoder(bool fail = false) => Services(fail).GetRequiredService<IGeocoder>();

    private IWeatherForecast Forecast(bool fail = false) => Services(fail).GetRequiredService<IWeatherForecast>();

    private ServiceProvider Services(bool fail)
    {
        var baseUrl = _server.Url("/").ToString().TrimEnd('/');
        var suffix = fail ? "?fail=1" : string.Empty;
        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:DataDirectory"] = Path.GetTempPath(),
                    ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}",
                    ["Weather:GeocodingUrl"] = baseUrl + "/search" + suffix,
                    ["Weather:ForecastUrl"] = baseUrl + "/v1/forecast" + suffix,
                })
                .Build())
            .BuildServiceProvider();
    }
}
