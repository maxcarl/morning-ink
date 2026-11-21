namespace MorningInk.Domain;

public class Section
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProfileId { get; init; }

    public required string Name { get; set; }

    public int Position { get; set; }

    /// <summary>Most articles this section may contain; null means no limit.</summary>
    public int? MaxArticles { get; set; }

    public ICollection<Source> Sources { get; } = [];
}
