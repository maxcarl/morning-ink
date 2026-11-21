namespace MorningInk.Infrastructure.Sources;

/// <summary>News search for local news. Configured in the "NewsSearch" section.</summary>
public sealed class NewsSearchOptions
{
    public const string SectionName = "NewsSearch";

    /// <summary>Google News RSS search endpoint. The search terms (towns) go to Google.</summary>
    public string GoogleNewsUrl { get; set; } = "https://news.google.com/rss/search";

    /// <summary>Language of the results.</summary>
    public string Language { get; set; } = "de";

    /// <summary>Country edition, as ISO code.</summary>
    public string Country { get; set; } = "DE";
}
