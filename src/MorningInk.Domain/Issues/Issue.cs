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

    /// <summary>Last delivery error in English, for logs and as a fallback.</summary>
    public string? Error { get; set; }

    /// <summary>Code of the last delivery error, e.g. "Unreachable"; the dashboard shows it in the user's language.</summary>
    public string? ErrorReason { get; set; }

    /// <summary>The values the error's words need, e.g. the mail server's name.</summary>
    public List<string> ErrorArguments { get; set; } = [];

    /// <summary>How the issue was built and sent, step by step, for the archive and the dashboard.</summary>
    public List<IssueLogEntry> Log { get; set; } = [];

    public void AddLog(DateTimeOffset at, string @event, params IEnumerable<object> arguments) =>
        Log.Add(new IssueLogEntry { At = at, Event = @event, Arguments = [.. arguments.Select(a => Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)] });

    public void SetError(string? message, string? reason, IReadOnlyList<string> arguments)
    {
        Error = message;
        ErrorReason = reason;
        ErrorArguments = [.. arguments];
    }

    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Articles in the issue; after delivery they count as seen.</summary>
    public ICollection<IssueEntry> Entries { get; } = [];

    public ICollection<DeliveryAttempt> Attempts { get; } = [];
}
