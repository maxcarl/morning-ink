namespace MorningInk.Domain;

/// <summary>
/// One issue ("Heft") of a profile. There is exactly one per profile and scheduled time, which is what keeps an
/// issue from ever being sent twice.
/// </summary>
public class Issue
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid ProfileId { get; init; }

    /// <summary>Running number within the profile, starting at 1.</summary>
    public required int Number { get; init; }

    /// <summary>The slot this issue belongs to, in UTC; unique per profile.</summary>
    public required DateTimeOffset ScheduledFor { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public IssueStatus Status { get; set; } = IssueStatus.Building;

    /// <summary>Rendered file in the issue directory, named after <see cref="Id"/>; null until rendered.</summary>
    public string? FileName { get; set; }

    public long? FileSize { get; set; }

    /// <summary>Pinned issues survive the cleanup job.</summary>
    public bool Pinned { get; set; }

    /// <summary>Last delivery error, shown on the dashboard.</summary>
    public string? Error { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Articles in the issue; after delivery they count as seen.</summary>
    public ICollection<IssueEntry> Entries { get; } = [];

    public ICollection<DeliveryAttempt> Attempts { get; } = [];
}
