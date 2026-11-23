using System.Globalization;
using System.Text;
using System.Text.Json;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Ai;

/// <summary>
/// Recognizes the edition's interests by what the articles are about: the AI gets the topics with their keywords as
/// hints and the new articles' titles and teasers, all as numbered data, and names the topics of each article.
/// </summary>
/// <remarks>
/// Articles go in batches, so a small local model keeps its context and one bad answer costs only its batch. Every
/// batch is a request and counts against the daily limit. Articles that must stay local never go to a cloud provider.
/// </remarks>
internal sealed class AiInterestMatcher(AiRequests ai) : IInterestMatcher
{
    private const int BatchSize = 40;
    private const int TeaserCharacters = 200;
    private const int MaxTopicCharacters = 1000;
    private const int MaxOutputTokens = 1500;

    /// <summary>The versioned default instructions, which the admin may replace on the settings page.</summary>
    public static readonly string DefaultPrompt = AiText.Prompt("interests.txt");

    // Always appended, also to the admin's own instructions: the answer's form and topics and articles as data only.
    private static readonly string Format = AiText.Prompt("interests-format.txt");

    public async Task<IReadOnlyList<InterestMatch>?> MatchAsync(InterestRequest request, CancellationToken cancellationToken)
    {
        if (request.Interests.Count == 0 || await ai.SettingsAsync(cancellationToken) is not { RecognizeInterests: true } settings)
        {
            return null;
        }

        var offered = request.Articles.Where(a => settings.IsLocal || !a.LocalAiOnly).Select(a => a.Article).ToList();
        if (offered.Count == 0)
        {
            return null;
        }

        var instructions = $"{settings.InterestsPrompt ?? DefaultPrompt}\n\n{Format}";
        var topics = Topics(request.Interests);
        List<InterestMatch>? matches = null;
        foreach (var batch in offered.Chunk(BatchSize))
        {
            var question = new AiQuestion(
                instructions,
                $"<topics>\n{topics}\n</topics>\n\n<articles>\n{AiText.Numbered(batch, TeaserCharacters)}\n</articles>",
                MaxOutputTokens,
                request.UserId,
                request.Edition,
                $"Interests in {batch.Length} articles");
            if (await ai.AskAsync(settings, question, answer => Checked(answer, batch, request.Interests), cancellationToken) is { } found)
            {
                (matches ??= []).AddRange(found);
            }
        }

        return matches;
    }

    // "[1] Football (keywords: Bundesliga, DFB)": the name says what it is about, the keywords are hints.
    private static string Topics(IReadOnlyList<Interest> interests)
    {
        var text = new StringBuilder();
        for (var i = 0; i < interests.Count; i++)
        {
            var interest = interests[i];
            var keywords = interest.Keywords.Count == 0 ? string.Empty : $" (keywords: {string.Join(", ", interest.Keywords.Select(k => k.TrimEnd('*')))})";
            text.Append(CultureInfo.InvariantCulture, $"[{i + 1}] {AiText.Shorten(AiText.Plain(interest.Topic + keywords), MaxTopicCharacters)}\n");
        }

        return text.ToString().TrimEnd();
    }

    // Untrusted like every answer: articles and topics only by numbers that were given; anything else and the batch
    // counts as unanswered.
    private static List<InterestMatch>? Checked(string? answer, FetchedArticle[] batch, IReadOnlyList<Interest> interests)
    {
        if (AiText.Entries(answer, "matches") is not { } entries)
        {
            return null;
        }

        var matches = new List<InterestMatch>();
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("article", out var number) || AiText.Number(number, batch.Length) is not { } index
                || !entry.TryGetProperty("topics", out var topics) || topics.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var topic in topics.EnumerateArray())
            {
                if (AiText.Number(topic, interests.Count) is not { } topicIndex)
                {
                    return null;
                }

                matches.Add(new InterestMatch(batch[index - 1], interests[topicIndex - 1]));
            }
        }

        return matches;
    }
}
