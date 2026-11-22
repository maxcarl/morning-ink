namespace MorningInk.Application.Sources;

/// <summary>An article as a source delivered it. All text is untrusted and still unsanitized.</summary>
/// <param name="Id">Identifier given by the source (RSS guid, Atom id), if any.</param>
/// <param name="Title">Headline as plain text.</param>
/// <param name="Url">Absolute http or https link to the original.</param>
/// <param name="PublishedAt">Publication time, or null if the source gave none or an unreadable one.</param>
/// <param name="Author">Author name, if given.</param>
/// <param name="Summary">Teaser, often HTML.</param>
/// <param name="Content">Full text, often HTML; many feeds only have a summary.</param>
/// <param name="AiSummary">A few plain sentences written by the AI, checked before use; null without AI.</param>
/// <param name="Categories">The publisher's categories or tags.</param>
/// <param name="MediaType">Type of an attached file (RSS enclosure), e.g. "video/mp4" for a programme.</param>
public sealed record FetchedArticle(
    string? Id,
    string Title,
    Uri Url,
    DateTimeOffset? PublishedAt,
    string? Author,
    string? Summary,
    string? Content,
    string? AiSummary = null,
    IReadOnlyList<string>? Categories = null,
    string? MediaType = null);
