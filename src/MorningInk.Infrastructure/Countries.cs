using System.Globalization;

namespace MorningInk.Infrastructure;

/// <summary>Countries for profiles and local sources: validation, the list for the UI and the language of their news.</summary>
public static class Countries
{
    // The language a country's news and place names are in. .NET knows every country but not its leading language
    // (Austria also has an "en-AT" culture), so the common ones are listed; others fall back to a configured language.
    private static readonly Dictionary<string, string> NewsLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = "de",
        ["AT"] = "de",
        ["CH"] = "de",
        ["LI"] = "de",
        ["LU"] = "de",
        ["US"] = "en",
        ["GB"] = "en",
        ["IE"] = "en",
        ["CA"] = "en",
        ["AU"] = "en",
        ["NZ"] = "en",
        ["FR"] = "fr",
        ["BE"] = "nl",
        ["NL"] = "nl",
        ["ES"] = "es",
        ["MX"] = "es",
        ["IT"] = "it",
        ["PT"] = "pt",
        ["BR"] = "pt",
        ["PL"] = "pl",
        ["CZ"] = "cs",
        ["SE"] = "sv",
        ["NO"] = "no",
        ["DK"] = "da",
        ["FI"] = "fi",
    };

    /// <summary>Language of news and place names in <paramref name="country"/>, or null if unknown.</summary>
    public static string? LanguageOf(string? country) =>
        country is not null && NewsLanguages.TryGetValue(country, out var language) ? language : null;

    /// <summary>Accepts an ISO code like "de" or "DE" and returns it in upper case; empty input means "worldwide" (null).</summary>
    public static bool TryNormalize(string? input, out string? country)
    {
        country = null;
        var code = input?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            return true;
        }

        if (code.Length != 2 || !code.All(char.IsAsciiLetterUpper) || !Regions.ContainsKey(code))
        {
            return false;
        }

        country = code;
        return true;
    }

    // Built from culture names like "ar-EH": a bare code can fail, because some country codes are also language codes ("EH").
    private static readonly Dictionary<string, RegionInfo> Regions = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(culture => new RegionInfo(culture.Name))
        .Where(region => region.TwoLetterISORegionName.Length == 2 && region.TwoLetterISORegionName.All(char.IsAsciiLetterUpper))
        .DistinctBy(region => region.TwoLetterISORegionName)
        .ToDictionary(region => region.TwoLetterISORegionName, StringComparer.Ordinal);

    /// <summary>All country codes .NET knows.</summary>
    public static IReadOnlyCollection<string> All => Regions.Keys;

    /// <summary>Codes with names in the current UI language, sorted by name, for a selection list.</summary>
    public static IReadOnlyList<(string Code, string Name)> ForDisplay() => Regions.Values
        .Select(region => (Code: region.TwoLetterISORegionName, Name: region.DisplayName))
        .OrderBy(country => country.Name, StringComparer.Create(CultureInfo.CurrentUICulture, ignoreCase: true))
        .ToList();
}
