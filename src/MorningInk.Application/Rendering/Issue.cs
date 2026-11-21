using MorningInk.Application.Sources;

namespace MorningInk.Application.Rendering;

/// <summary>Everything one newspaper issue contains.</summary>
/// <param name="Id">Stable identifier of the issue; readers use it to tell issues apart.</param>
/// <param name="Title">Newspaper title from the profile, e.g. "Morning Ink".</param>
/// <param name="Number">Running issue number of the profile.</param>
/// <param name="PublishedAt">Issue time in the profile's time zone; dates on pages use this offset.</param>
/// <param name="Language">Culture name for texts and metadata, e.g. "de".</param>
/// <param name="Sections">Sections in order, each with its articles.</param>
/// <param name="Notes">Hints shown on the title page, e.g. which sources failed today.</param>
public sealed record Issue(
    Guid Id,
    string Title,
    int Number,
    DateTimeOffset PublishedAt,
    string Language,
    IReadOnlyList<IssueSection> Sections,
    IReadOnlyList<string> Notes);

/// <summary>A section of an issue.</summary>
public sealed record IssueSection(string Name, IReadOnlyList<IssueArticle> Articles);

/// <summary>An article in an issue, with the name of the source it came from.</summary>
public sealed record IssueArticle(FetchedArticle Article, string SourceName);
