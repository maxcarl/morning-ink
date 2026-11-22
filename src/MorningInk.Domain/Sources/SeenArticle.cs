namespace MorningInk.Domain;

/// <summary>An article a user has already received, so it never appears twice.</summary>
public class SeenArticle
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    /// <summary>SHA-256 of the <see cref="UrlNormalizer"/> key, as lowercase hex.</summary>
    public required string UrlHash { get; init; }

    /// <summary>When the article was first delivered; the cleanup job removes old entries.</summary>
    public required DateTimeOffset SeenAt { get; init; }
}
