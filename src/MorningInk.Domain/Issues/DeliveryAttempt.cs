namespace MorningInk.Domain;

/// <summary>One try to deliver an issue, kept for the dashboard.</summary>
public class DeliveryAttempt
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid IssueId { get; init; }

    public required DateTimeOffset AttemptedAt { get; init; }

    public required bool Succeeded { get; init; }

    /// <summary>Why it failed; null on success. Never contains credentials.</summary>
    public string? Error { get; init; }
}
