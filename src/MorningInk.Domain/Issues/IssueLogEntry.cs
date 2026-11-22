namespace MorningInk.Domain;

/// <summary>One step of building and sending an issue, e.g. "Fetched" with the source and the number of articles.</summary>
/// <remarks>A code with values rather than text, so the UI shows it in the user's language.</remarks>
public sealed class IssueLogEntry
{
    public DateTimeOffset At { get; set; }

    public string Event { get; set; } = string.Empty;

    public List<string> Arguments { get; set; } = [];
}
