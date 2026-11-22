namespace MorningInk.Domain;

/// <summary>A host in the home network an admin allowed Morning Ink to reach, e.g. a FreshRSS instance.</summary>
public class AllowedHost
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>"host" (any port) or "host:port", lower case.</summary>
    public required string Entry { get; init; }

    /// <summary>The admin who allowed it.</summary>
    public required Guid AddedBy { get; init; }

    public required DateTimeOffset AddedAt { get; init; }
}
