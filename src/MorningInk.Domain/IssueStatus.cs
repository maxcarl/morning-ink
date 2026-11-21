namespace MorningInk.Domain;

/// <summary>Where an issue stands.</summary>
public enum IssueStatus
{
    /// <summary>Being built; a crash in this state means the next run builds it again.</summary>
    Building,

    /// <summary>File written, not delivered yet.</summary>
    Rendered,

    /// <summary>Delivered; it is never sent again.</summary>
    Delivered,

    /// <summary>Delivery failed; the next run sends the same file again.</summary>
    Failed,

    /// <summary>No new articles, so nothing was sent.</summary>
    Empty,
}
