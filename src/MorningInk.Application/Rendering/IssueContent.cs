using MorningInk.Application.Sources;
using MorningInk.Application.Weather;

namespace MorningInk.Application.Rendering;

/// <summary>Everything one newspaper issue contains, ready for a renderer.</summary>
/// <param name="Id">Stable identifier of the issue; readers use it to tell issues apart.</param>
/// <param name="Title">Newspaper title from the profile, e.g. "Morning Ink".</param>
/// <param name="Number">Running issue number of the profile.</param>
/// <param name="PublishedAt">Issue time in the profile's time zone; dates on pages use this offset.</param>
/// <param name="Language">Culture name for texts and metadata, e.g. "de".</param>
/// <param name="Sections">Sections in order, each with its articles.</param>
/// <param name="UnavailableSources">Names of sources that failed today; the title page mentions them.</param>
/// <param name="Weather">Forecast for the profile's region; null leaves the weather out.</param>
public sealed record IssueContent(
    Guid Id,
    string Title,
    int Number,
    DateTimeOffset PublishedAt,
    string Language,
    IReadOnlyList<IssueSection> Sections,
    IReadOnlyList<string> UnavailableSources,
    WeatherForecast? Weather = null)
{
    /// <summary>
    /// Title of the book, e.g. "Morning Ink 2026-11-21": the ISO date makes it unique per day and sorts by date on
    /// the Kindle. Used for the metadata and for the attachment name, which Send to Kindle may show instead.
    /// </summary>
    public string BookTitle => BookTitleFor(Title, PublishedAt);

    /// <summary>The <see cref="BookTitle"/> of an issue with this title and time.</summary>
    public static string BookTitleFor(string title, DateTimeOffset publishedAt) => $"{title} {publishedAt:yyyy-MM-dd}";
}

/// <summary>A section of an issue.</summary>
public sealed record IssueSection(string Name, IReadOnlyList<IssueArticle> Articles);

/// <summary>An article in an issue, with the name of the source it came from.</summary>
/// <param name="AlsoAt">The same story from other sources, left out of the issue and named under the article.</param>
public sealed record IssueArticle(FetchedArticle Article, string SourceName, IReadOnlyList<AlsoReported>? AlsoAt = null)
{
    public IReadOnlyList<AlsoReported> AlsoAt { get; init; } = AlsoAt ?? [];
}

/// <summary>Another source's article about the same story.</summary>
public sealed record AlsoReported(string SourceName, FetchedArticle Article);
