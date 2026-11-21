namespace MorningInk.Domain;

public class Source
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required string Name { get; set; }

    public SourceKind Kind { get; set; } = SourceKind.Rss;

    public required string Url { get; set; }
}
