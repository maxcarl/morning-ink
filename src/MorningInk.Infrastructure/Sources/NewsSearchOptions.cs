namespace MorningInk.Infrastructure.Sources;

/// <summary>News search for local news. Configured in the "NewsSearch" section.</summary>
public sealed class NewsSearchOptions
{
    public const string SectionName = "NewsSearch";

    /// <summary>Bing News search endpoint (RSS). The search terms (towns) go to Microsoft.</summary>
    public string BingNewsUrl { get; set; } = "https://www.bing.com/news/search";

    /// <summary>
    /// Address of one's own SearXNG, e.g. "https://searx.example.com"; set, it replaces Bing and the towns stay with
    /// the own instance. In the home network its host must be allowed like any other.
    /// </summary>
    public string? SearxngUrl { get; set; }

    /// <summary>Language of the results.</summary>
    public string Language { get; set; } = "de";

    /// <summary>Country edition, as ISO code.</summary>
    public string Country { get; set; } = "DE";
}
