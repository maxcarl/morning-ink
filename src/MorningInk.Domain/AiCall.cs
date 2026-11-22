namespace MorningInk.Domain;

/// <summary>
/// One request to the AI provider: what went where, so the admin can see which content left the instance.
/// Holds the article's title and address, never its text.
/// </summary>
public class AiCall
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required DateTimeOffset At { get; init; }

    /// <summary>The provider's host and port, e.g. "192.168.1.20:11434" or "openrouter.ai:443".</summary>
    public required string Provider { get; init; }

    public required string Model { get; init; }

    /// <summary>Whether the provider counted as local when the call was made.</summary>
    public bool IsLocal { get; init; }

    /// <summary>The user whose source the article came from.</summary>
    public required Guid UserId { get; init; }

    public required string SourceName { get; init; }

    public required string ArticleTitle { get; init; }

    public required string ArticleUrl { get; init; }

    /// <summary>Characters of article text sent.</summary>
    public int Characters { get; init; }

    /// <summary>False when the provider failed or gave no usable answer.</summary>
    public bool Succeeded { get; init; }
}
