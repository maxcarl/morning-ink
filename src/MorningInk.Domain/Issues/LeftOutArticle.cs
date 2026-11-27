namespace MorningInk.Domain;

/// <summary>
/// An article a filter kept out of an issue, so the archive can show what was missed and the filter can be fixed.
/// Not an <see cref="IssueEntry"/> on purpose: entries count as seen, and a wrongly filtered article must be able to
/// come with the next issue once the filter is changed.
/// </summary>
public sealed class LeftOutArticle
{
    public string Section { get; set; } = string.Empty;

    public string SourceName { get; set; } = string.Empty;

    /// <summary>The source it came from, to open its filter; it may have been deleted since.</summary>
    public Guid? SourceId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>Why, e.g. "Excluded", "TitlePattern", "TooShort" or "Interest"; the archive words it.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>The keyword or interest that decided, where there is one.</summary>
    public string? Detail { get; set; }
}
