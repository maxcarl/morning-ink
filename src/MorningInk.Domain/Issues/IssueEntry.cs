namespace MorningInk.Domain;

/// <summary>An article contained in an issue.</summary>
public class IssueEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid IssueId { get; init; }

    public required string Url { get; init; }

    public required string Title { get; init; }

    public required string SourceName { get; init; }

    /// <summary>The source it came from, so the source can be told once the issue is out (e.g. FreshRSS marks it read).</summary>
    public Guid? SourceId { get; init; }

    /// <summary>The article's id at its source, e.g. FreshRSS's item id; null when the source has none.</summary>
    public string? ExternalId { get; init; }

    /// <summary>The same story as another entry, from another source: not in the issue itself, but counted as seen.</summary>
    public bool Duplicate { get; init; }
}
