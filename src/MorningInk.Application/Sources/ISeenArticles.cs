namespace MorningInk.Application.Sources;

/// <summary>
/// Remembers which articles a user has received. Filtering and marking are separate on purpose:
/// articles are marked only after the issue was delivered, so a failed delivery loses nothing.
/// </summary>
public interface ISeenArticles
{
    /// <summary>
    /// Returns the articles the user has not received yet, each article only once even if several sources
    /// delivered it. Records nothing.
    /// </summary>
    Task<IReadOnlyList<FetchedArticle>> FilterNewAsync(Guid userId, IReadOnlyList<FetchedArticle> articles, CancellationToken cancellationToken);

    /// <summary>Records the articles as received. Marking an article again is harmless.</summary>
    Task MarkSeenAsync(Guid userId, IReadOnlyList<FetchedArticle> articles, CancellationToken cancellationToken);
}
