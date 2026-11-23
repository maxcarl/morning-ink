using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Processing;

/// <summary>
/// Recognizes an edition's interests in articles by what they are about, not only by their keywords: "Kane scores
/// twice for Bayern" is football without the word.
/// </summary>
public interface IInterestMatcher
{
    /// <summary>Which article is about which interest; null when no AI is set up, allowed or reachable.</summary>
    Task<IReadOnlyList<InterestMatch>?> MatchAsync(InterestRequest request, CancellationToken cancellationToken);
}

/// <param name="UserId">Whose edition it is, for the record of what went to the AI.</param>
/// <param name="Edition">The edition's title, for the same record.</param>
/// <param name="Interests">The topics to recognize, each described by its name and keywords.</param>
public sealed record InterestRequest(Guid UserId, string Edition, IReadOnlyList<Interest> Interests, IReadOnlyList<InterestCandidate> Articles);

/// <param name="Article">A new article, as its source delivered it: title and teaser are what the AI reads.</param>
/// <param name="LocalAiOnly">From a source whose content may only go to an AI on the user's own hardware.</param>
public sealed record InterestCandidate(FetchedArticle Article, bool LocalAiOnly);

public sealed record InterestMatch(FetchedArticle Article, Interest Interest);
