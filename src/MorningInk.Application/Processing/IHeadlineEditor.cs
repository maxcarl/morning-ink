using MorningInk.Application.Rendering;

namespace MorningInk.Application.Processing;

/// <summary>Rewrites lurid headlines of an issue plainly ("neutral headlines"); the original stays with the article.</summary>
public interface IHeadlineEditor
{
    /// <summary>
    /// Only the headlines that needed a new one; null when the step is off, no AI is set up, allowed or reachable.
    /// </summary>
    Task<IReadOnlyList<Headline>?> RewriteAsync(IssueAiRequest request, CancellationToken cancellationToken);
}

/// <param name="Title">The plain headline, as plain text.</param>
public sealed record Headline(IssueArticle Article, string Title);
