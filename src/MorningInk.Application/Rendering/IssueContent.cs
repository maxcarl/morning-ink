using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
using MorningInk.Application.Wikipedia;

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
/// <param name="Extras">Pages that are no articles section (overview, weather, on this day), each with its place among the sections.</param>
public sealed record IssueContent(
    Guid Id,
    string Title,
    int Number,
    DateTimeOffset PublishedAt,
    string Language,
    IReadOnlyList<IssueSection> Sections,
    IReadOnlyList<string> UnavailableSources,
    WeatherForecast? Weather = null,
    IReadOnlyList<IssueExtra>? Extras = null)
{
    public IReadOnlyList<IssueExtra> Extras { get; init; } = Extras ?? [];

    /// <summary>
    /// "More of this" and "less of this" per article, by reference; null when readers cannot reach the instance or
    /// for a preview, which has no entries to give feedback on.
    /// </summary>
    public IReadOnlyDictionary<IssueArticle, FeedbackLinks>? Feedback { get; init; }

    /// <summary>Whether the weather opens the issue on its title page, or has a page of its own further down.</summary>
    public bool WeatherOnTitlePage
    {
        get
        {
            if (Weather is null)
            {
                return false;
            }

            // Without a place of its own, or placed first of all, the weather opens the issue.
            var placed = Extras.FirstOrDefault(e => e.Kind == IssueExtraKind.Weather);
            return placed is null || (placed.Before == 0 && ReferenceEquals(Extras[0], placed));
        }
    }

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
public sealed record IssueArticle(FetchedArticle Article, string SourceName, IReadOnlyList<AlsoReported>? AlsoAt = null, Guid? SourceId = null)
{
    public IReadOnlyList<AlsoReported> AlsoAt { get; init; } = AlsoAt ?? [];
}

/// <summary>Another source's article about the same story.</summary>
public sealed record AlsoReported(string SourceName, FetchedArticle Article, Guid? SourceId = null);

public enum IssueExtraKind
{
    Overview,
    Weather,
    OnThisDay,
}

/// <summary>A page that is no articles section, with its place in the issue.</summary>
/// <param name="Before">Index of the articles section it comes before; the number of sections puts it at the end.</param>
/// <param name="Overview">The overview's paragraph and articles; null for the others. The weather is in <see cref="IssueContent.Weather"/>.</param>
/// <param name="Events">The anniversaries of "on this day"; null for the others.</param>
public sealed record IssueExtra(IssueExtraKind Kind, string Title, int Before, IssueOverview? Overview = null, IReadOnlyList<Anniversary>? Events = null);

/// <summary>The overview: a paragraph about the day's main news, then links to their articles, most important first.</summary>
public sealed record IssueOverview(string Summary, IReadOnlyList<IssueArticle> Articles);

/// <summary>Signed links for the reader to say "more of this" or "less of this" about an article.</summary>
public sealed record FeedbackLinks(Uri More, Uri Less);

/// <summary>Makes the feedback links for an entry of an issue; null when readers cannot reach the instance.</summary>
public interface IFeedbackLinks
{
    FeedbackLinks? LinksFor(Guid entryId);
}
