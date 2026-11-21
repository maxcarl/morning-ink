namespace MorningInk.Domain;

/// <summary>An article contained in an issue.</summary>
public class IssueEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid IssueId { get; init; }

    public required string Url { get; init; }

    public required string Title { get; init; }

    public required string SourceName { get; init; }
}
