using MorningInk.Application.Processing;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Publishing;

/// <summary>
/// The AI steps that look at a whole issue rather than one article: recognize the interests before choosing, then
/// plain headlines and the overview once the articles are processed. Each is a bonus: without AI, or when it fails,
/// the issue comes without it, and the log says what happened.
/// </summary>
public sealed class IssueAiSteps(IInterestMatcher matcher, IHeadlineEditor headlines, IIssueOverview overview, TimeProvider time)
{
    /// <summary>
    /// The edition's interests, plus the reader's recent answers as two more for the AI: "like these" counts as
    /// preferred, "unlike these" as less. Neither has keywords, so without AI they never match.
    /// </summary>
    public static EditionInterests InterestsOf(PublishingProfile loaded)
    {
        var liked = loaded.LikedTitles.Count == 0 ? null : FromFeedback("Similar to these articles the reader wanted more of", loaded.LikedTitles, InterestWeight.Prefer);
        var disliked = loaded.DislikedTitles.Count == 0 ? null : FromFeedback("Similar to these articles the reader wanted less of", loaded.DislikedTitles, InterestWeight.Less);
        IReadOnlyList<Interest> all = [.. loaded.Profile.Interests, .. new[] { liked, disliked }.OfType<Interest>()];
        return new EditionInterests(all, liked, disliked);
    }

    /// <summary>Which candidate is about which interest according to the AI, by reference; empty without AI.</summary>
    /// <param name="candidates">The new articles that pass their source's filters, each with its source.</param>
    public async Task<IReadOnlyDictionary<FetchedArticle, HashSet<Interest>>> RecognizeAsync(
        PublishingProfile loaded,
        EditionInterests interests,
        IReadOnlyList<(FetchedArticle Article, Source Source)> candidates,
        bool withAi,
        Issue issue,
        CancellationToken cancellationToken)
    {
        var recognized = new Dictionary<FetchedArticle, HashSet<Interest>>(ReferenceEqualityComparer.Instance);
        if (interests.All.Count == 0 || candidates.Count == 0)
        {
            return recognized;
        }

        if (withAi)
        {
            var request = new InterestRequest(loaded.Profile.UserId, loaded.Profile.Title, interests.All, [.. candidates.Select(c => new InterestCandidate(c.Article, c.Source.LocalAiOnly))]);
            foreach (var match in await Guarded(() => matcher.MatchAsync(request, cancellationToken)) ?? [])
            {
                if (!recognized.TryGetValue(match.Article, out var found))
                {
                    recognized[match.Article] = found = [];
                }

                found.Add(match.Interest);
            }

            if (recognized.Count > 0)
            {
                issue.AddLog(time.GetUtcNow(), "InterestsRecognized", recognized.Count);
            }
        }

        // Keywords and AI together: the log says which interest took how many of the new articles.
        foreach (var interest in interests.All)
        {
            var count = candidates.Count(c => Interests.Matches(c.Article, interest, recognized.GetValueOrDefault(c.Article)));
            if (count == 0)
            {
                continue;
            }

            if (ReferenceEquals(interest, interests.Liked))
            {
                issue.AddLog(time.GetUtcNow(), "LikedMatched", count);
            }
            else if (ReferenceEquals(interest, interests.Disliked))
            {
                issue.AddLog(time.GetUtcNow(), "DislikedMatched", count);
            }
            else
            {
                issue.AddLog(time.GetUtcNow(), "InterestMatched", interest.Topic, count);
            }
        }

        return recognized;
    }

    /// <summary>The content with plain headlines where the AI rewrote lurid ones; the original stays with each article.</summary>
    public async Task<List<(Section Section, IssueSection Content)>> HeadlinesAsync(
        List<(Section Section, IssueSection Content)> content,
        PublishingProfile loaded,
        Issue issue,
        CancellationToken cancellationToken)
    {
        var rewritten = await Guarded(() => headlines.RewriteAsync(Request(content, loaded), cancellationToken));
        if (rewritten is not { Count: > 0 })
        {
            return content;
        }

        var titles = rewritten.ToDictionary<Headline, IssueArticle, string>(h => h.Article, h => h.Title, ReferenceEqualityComparer.Instance);
        issue.AddLog(time.GetUtcNow(), "Headlines", titles.Count);
        return
        [
            .. content.Select(c => (c.Section, c.Content with
            {
                Articles = [.. c.Content.Articles.Select(a => titles.TryGetValue(a, out var title)
                    ? a with { Article = a.Article with { Title = title, OriginalTitle = a.Article.Title } }
                    : a)],
            })),
        ];
    }

    /// <summary>The issue's main news, "the most important in 5 minutes"; null without AI or when it fails.</summary>
    public async Task<IReadOnlyList<OverviewItem>?> OverviewAsync(List<(Section Section, IssueSection Content)> content, PublishingProfile loaded, Issue issue, CancellationToken cancellationToken)
    {
        var items = await Guarded(() => overview.WriteAsync(Request(content, loaded), cancellationToken));
        issue.AddLog(time.GetUtcNow(), items is { Count: > 0 } ? "Overview" : "OverviewSkipped", items?.Count ?? 0);
        return items;
    }

    // Each article with whether its source must stay with a local AI.
    private static IssueAiRequest Request(List<(Section Section, IssueSection Content)> content, PublishingProfile loaded) =>
        new(loaded.Profile.UserId, loaded.Profile.Title, loaded.Language,
        [
            .. content.SelectMany(c => c.Content.Articles.Select(a => new AiCandidate(a, c.Section.Sources.FirstOrDefault(s => s.Id == a.SourceId)?.LocalAiOnly ?? false))),
        ]);

    private static Interest FromFeedback(string description, IReadOnlyList<string> titles, InterestWeight weight) =>
        new() { Topic = $"{description}: {string.Join("; ", titles.Select(t => $"\"{t}\""))}", Weight = weight };

    // A bug in an AI step must not stop the newspaper: it counts as no answer.
    private static async Task<T?> Guarded<T>(Func<Task<T?>> step)
        where T : class
    {
        try
        {
            return await step();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }
}

/// <param name="All">What the articles are sorted and filtered by.</param>
/// <param name="Liked">The reader's "more of this" as an interest, if there was any lately.</param>
/// <param name="Disliked">Their "less of this", likewise.</param>
public sealed record EditionInterests(IReadOnlyList<Interest> All, Interest? Liked, Interest? Disliked);
