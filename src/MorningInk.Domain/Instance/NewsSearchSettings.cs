namespace MorningInk.Domain;

/// <summary>How local news are searched for the whole instance. One row at most.</summary>
public class NewsSearchSettings
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>One's own SearXNG, e.g. "https://searx.example.com"; null searches with Bing.</summary>
    public string? SearxngUrl { get; set; }
}
