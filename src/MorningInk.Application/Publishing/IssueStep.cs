namespace MorningInk.Application.Publishing;

/// <summary>The parts of a run, in order.</summary>
public enum IssueStage
{
    /// <summary>Asking each source for its articles.</summary>
    Fetching,

    /// <summary>The AI matching new articles to the edition's interests.</summary>
    Interests,

    /// <summary>Full text, cleaning, images and AI summaries per article: usually the longest part.</summary>
    Processing,

    /// <summary>The AI rewriting lurid headlines.</summary>
    Headlines,

    /// <summary>Weather, the AI overview and "on this day".</summary>
    Extras,

    Rendering,
    Sending,
}

/// <summary>Where a run stands; shown on the status page and logged, so a slow run can be told from a stuck one.</summary>
/// <param name="Done">Sources or articles done in this stage, where it counts them.</param>
/// <param name="Total">Of how many; zero where the stage does not count.</param>
public sealed record IssueStep(IssueStage Stage, int Done = 0, int Total = 0);
