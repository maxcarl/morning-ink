namespace MorningInk.Domain;

/// <summary>Where articles come from, e.g. an RSS feed.</summary>
public class Source
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required string Name { get; set; }

    public SourceKind Kind { get; set; } = SourceKind.Rss;

    /// <summary>Address of the source. Untrusted input: only fetched through the safe HTTP client.</summary>
    public required string Url { get; set; }

    /// <summary>Login used to fetch the source; null for public sources.</summary>
    public Guid? CredentialId { get; set; }
}
