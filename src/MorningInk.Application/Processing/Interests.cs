using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Processing;

/// <summary>An edition's interests applied to an article: a score to sort by, and topics to leave out.</summary>
/// <remarks>
/// An article is about an interest when one of its keywords is in headline or teaser, or when the AI recognized the
/// topic in it. Without AI the keywords decide alone.
/// </remarks>
public static class Interests
{
    /// <param name="recognized">The interests the AI found in this article; null without AI.</param>
    public static int Score(FetchedArticle article, IReadOnlyList<Interest> interests, IReadOnlySet<Interest>? recognized = null) =>
        interests.Where(i => Matches(article, i, recognized)).Sum(i => i.Weight switch
        {
            InterestWeight.StrongPrefer => 2,
            InterestWeight.Prefer => 1,
            InterestWeight.Less => -1,
            _ => 0,
        });

    /// <param name="recognized">The interests the AI found in this article; null without AI.</param>
    public static bool Hides(FetchedArticle article, IReadOnlyList<Interest> interests, IReadOnlySet<Interest>? recognized = null) =>
        Hiding(article, interests, recognized) is not null;

    /// <summary>The interest set to hide that leaves the article out, or null.</summary>
    /// <param name="recognized">The interests the AI found in this article; null without AI.</param>
    public static Interest? Hiding(FetchedArticle article, IReadOnlyList<Interest> interests, IReadOnlySet<Interest>? recognized = null) =>
        interests.FirstOrDefault(i => i.Weight == InterestWeight.Hide && Matches(article, i, recognized));

    public static bool Matches(FetchedArticle article, Interest interest, IReadOnlySet<Interest>? recognized = null) =>
        recognized?.Contains(interest) == true || interest.Keywords.Any(keyword => ArticleFilter.Mentions(article, keyword));
}
