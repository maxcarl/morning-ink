namespace MorningInk.Domain;

/// <summary>What the reader said about an article from the issue: more of this, or less.</summary>
/// <remarks>One per edition and article; saying the other later replaces it. Kept with title and address, so the AI
/// can learn from examples later.</remarks>
public class Feedback
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required Guid ProfileId { get; init; }

    /// <summary>The source the article came from; null once that source is deleted.</summary>
    public Guid? SourceId { get; set; }

    public required string SourceName { get; init; }

    public required string ArticleTitle { get; init; }

    public required string ArticleUrl { get; init; }

    public FeedbackKind Kind { get; set; }

    public DateTimeOffset At { get; set; }
}

public enum FeedbackKind
{
    More,
    Less,
}
