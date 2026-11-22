using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Processing;

/// <summary>An edition's interests applied to an article: a score to sort by, and topics to leave out.</summary>
/// <remarks>The keywords decide; an AI that recognizes a topic without its exact keywords can come later.</remarks>
public static class Interests
{
    public static int Score(FetchedArticle article, IReadOnlyList<Interest> interests) =>
        interests.Where(i => Matches(article, i)).Sum(i => i.Weight switch
        {
            InterestWeight.StrongPrefer => 2,
            InterestWeight.Prefer => 1,
            InterestWeight.Less => -1,
            _ => 0,
        });

    public static bool Hides(FetchedArticle article, IReadOnlyList<Interest> interests) =>
        interests.Any(i => i.Weight == InterestWeight.Hide && Matches(article, i));

    private static bool Matches(FetchedArticle article, Interest interest) =>
        interest.Keywords.Any(keyword => ArticleFilter.Mentions(article, keyword));
}
