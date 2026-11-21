namespace MorningInk.Domain;

/// <summary>A section of an issue (e.g. "Tech" or "Local") that collects articles from its sources.</summary>
public class Section
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProfileId { get; init; }

    public required string Name { get; set; }

    /// <summary>Order within the profile; lower comes first.</summary>
    public int Position { get; set; }

    /// <summary>Most articles this section may contain; null means no limit.</summary>
    public int? MaxArticles { get; set; }

    /// <summary>Sources feeding this section; a source can feed several sections.</summary>
    public ICollection<Source> Sources { get; } = [];
}
