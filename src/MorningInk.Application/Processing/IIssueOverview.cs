using MorningInk.Application.Rendering;

namespace MorningInk.Application.Processing;

/// <summary>Writes the overview of an issue, "the most important in 5 minutes", from its chosen articles.</summary>
public interface IIssueOverview
{
    /// <summary>The main news, most important first; null when no AI is set up, allowed or reachable.</summary>
    Task<IReadOnlyList<OverviewItem>?> WriteAsync(OverviewRequest request, CancellationToken cancellationToken);
}

/// <param name="UserId">Whose edition it is, for the record of what went to the AI.</param>
/// <param name="Edition">The edition's title, for the same record.</param>
/// <param name="Language">Culture name of the reader, e.g. "de": the overview is written in it.</param>
public sealed record OverviewRequest(Guid UserId, string Edition, string Language, IReadOnlyList<OverviewCandidate> Articles);

/// <param name="LocalAiOnly">From a source whose content may only go to an AI on the user's own hardware.</param>
public sealed record OverviewCandidate(IssueArticle Article, bool LocalAiOnly);
