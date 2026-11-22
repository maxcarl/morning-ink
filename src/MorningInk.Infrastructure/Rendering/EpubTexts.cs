using System.Globalization;
using System.Resources;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>Texts printed in issues, from EpubTexts.resx (English) and EpubTexts.de.resx (German).</summary>
internal sealed class EpubTexts(CultureInfo culture)
{
    private static readonly ResourceManager Resources = new("MorningInk.Infrastructure.Rendering.EpubTexts", typeof(EpubTexts).Assembly);

    public CultureInfo Culture { get; } = culture;

    /// <summary>Whether issue texts exist in <paramref name="language"/> (English is the built-in default).</summary>
    public static bool Exist(string language) =>
        language.Equals("en", StringComparison.OrdinalIgnoreCase)
        || Resources.GetResourceSet(CultureInfo.GetCultureInfo(language), createIfNotExists: true, tryParents: false) is not null;

    public string Contents => Get(nameof(Contents));

    public string TitlePage => Get(nameof(TitlePage));

    public string Headlines => Get(nameof(Headlines));

    public string Notes => Get(nameof(Notes));

    public string ReadOriginal => Get(nameof(ReadOriginal));

    public string Briefs => Get(nameof(Briefs));

    public string IssueNumber(int number) => string.Format(Culture, Get(nameof(IssueNumber)), number);

    public string ReadingTime(int minutes) => string.Format(Culture, Get(nameof(ReadingTime)), minutes);

    public string SourceUnavailable(string source) => string.Format(Culture, Get(nameof(SourceUnavailable)), source);

    public string WeatherFor(string place) => string.Format(Culture, Get(nameof(WeatherFor)), place);

    public string TemperatureRange(double min, double max) => string.Format(Culture, Get(nameof(TemperatureRange)), Degrees(min), Degrees(max));

    public string Temperature(double value) => string.Format(Culture, Get(nameof(Temperature)), Degrees(value));

    public string Precipitation(int percent) => string.Format(Culture, Get(nameof(Precipitation)), percent);

    public string DayPart(Application.Weather.DayPart part) => Get(part.ToString());

    /// <summary>Text for a WMO weather interpretation code as Open-Meteo reports it.</summary>
    public string Weather(int code) => Get(code switch
    {
        0 => "WeatherClear",
        1 => "WeatherMostlyClear",
        2 => "WeatherPartlyCloudy",
        3 => "WeatherOvercast",
        45 or 48 => "WeatherFog",
        51 or 53 or 55 => "WeatherDrizzle",
        56 or 57 or 66 or 67 => "WeatherFreezingRain",
        61 or 63 or 65 => "WeatherRain",
        71 or 73 or 75 or 77 => "WeatherSnow",
        80 or 81 or 82 => "WeatherRainShowers",
        85 or 86 => "WeatherSnowShowers",
        95 or 96 or 99 => "WeatherThunderstorm",
        _ => "WeatherUnknown",
    });

    // Whole degrees are enough on paper; away from zero, so 0.5 shows as 1 and not as 0.
    private static int Degrees(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Texts for <paramref name="language"/>; unknown languages fall back to English.</summary>
    public static EpubTexts For(string language)
    {
        try
        {
            return new EpubTexts(CultureInfo.GetCultureInfo(language));
        }
        catch (CultureNotFoundException)
        {
            return new EpubTexts(CultureInfo.GetCultureInfo("en"));
        }
    }

    private string Get(string name) =>
        Resources.GetString(name, Culture) ?? throw new InvalidOperationException($"Missing text {name}.");
}
