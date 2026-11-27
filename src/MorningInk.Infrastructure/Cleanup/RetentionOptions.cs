namespace MorningInk.Infrastructure.Cleanup;

/// <summary>How long Morning Ink keeps things. Configured in the "Retention" section.</summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>Issues and their files are deleted after this many days, unless pinned.</summary>
    public int IssueDays { get; set; } = 30;

    /// <summary>
    /// Seen articles are forgotten after this many days. Must stay longer than any source's maximum article age,
    /// or an old article could come again.
    /// </summary>
    public int SeenArticleDays { get; set; } = 90;

    /// <summary>Converted images are only needed until the issue is rendered; after that they live in the EPUB.</summary>
    public int ImageDays { get; set; } = 7;

    /// <summary>Database backups kept; older ones are deleted.</summary>
    public int KeepBackups { get; set; } = 5;

    /// <summary>
    /// When the cleanup runs each day, in UTC. Null (the default) leaves it to the status page; set, it wins there.
    /// </summary>
    public TimeOnly? CleanupTime { get; set; }
}
