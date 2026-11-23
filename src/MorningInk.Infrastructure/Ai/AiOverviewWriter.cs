using System.Globalization;
using System.Text.Json;
using MorningInk.Application.Processing;
using MorningInk.Application.Rendering;

namespace MorningInk.Infrastructure.Ai;

/// <summary>
/// Writes the issue's overview, "the most important in 5 minutes": the AI gets every chosen article as numbered data
/// and answers with the numbers of the main ones and a sentence each. Only what checks out is used: numbers of
/// articles in the issue, plain sentences of a sensible length.
/// </summary>
/// <remarks>
/// Optional like every AI step: without settings, at the daily limit or when the AI fails, there is no overview and
/// the issue comes without it. Articles that must stay local never go to a cloud provider.
/// </remarks>
internal sealed class AiOverviewWriter(AiRequests ai) : IIssueOverview
{
    private const int MinItems = 5;
    private const int MaxItems = 8;
    private const int MaxTextCharacters = 300;
    private const int TeaserCharacters = 400;
    private const int MaxOutputTokens = 1200;

    /// <summary>The versioned default instructions, which the admin may replace on the settings page.</summary>
    public static readonly string DefaultPrompt = AiText.Prompt("overview.txt");

    // Always appended, also to the admin's own instructions: the answer's form the check relies on, and the articles
    // as data only.
    private static readonly string Format = AiText.Prompt("overview-format.txt");

    public async Task<IReadOnlyList<OverviewItem>?> WriteAsync(IssueAiRequest request, CancellationToken cancellationToken)
    {
        if (await ai.SettingsAsync(cancellationToken) is not { } settings)
        {
            return null;
        }

        var offered = request.Articles.Where(a => settings.IsLocal || !a.LocalAiOnly).Select(a => a.Article).ToList();
        if (offered.Count == 0)
        {
            return null;
        }

        var instructions = $"{settings.OverviewPrompt ?? DefaultPrompt}\n\n{Format}"
            .Replace("{count}", string.Create(CultureInfo.InvariantCulture, $"{Math.Min(MinItems, offered.Count)} to {Math.Min(MaxItems, offered.Count)}"), StringComparison.Ordinal)
            .Replace("{language}", AiText.LanguageName(request.Language), StringComparison.Ordinal);
        var data = $"<articles>\n{AiText.Numbered([.. offered.Select(a => a.Article)], TeaserCharacters)}\n</articles>";
        var question = new AiQuestion(instructions, data, MaxOutputTokens, request.UserId, request.Edition, $"Overview of {offered.Count} articles");
        return await ai.AskAsync(settings, question, answer => Checked(answer, offered), cancellationToken);
    }

    // The answer is untrusted: each number an article offered and used once, each text a plain sentence; anything
    // else is no overview.
    private static List<OverviewItem>? Checked(string? answer, List<IssueArticle> offered)
    {
        if (AiText.Entries(answer, "items") is not { } entries)
        {
            return null;
        }

        var items = new List<OverviewItem>();
        foreach (var entry in entries.Take(MaxItems))
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("article", out var number) || AiText.Number(number, offered.Count) is not { } index
                || !entry.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var sentence = AiText.Sentences(text.GetString());
            var article = offered[index - 1];
            if (sentence.Length is 0 or > MaxTextCharacters || items.Any(i => ReferenceEquals(i.Article, article)))
            {
                return null;
            }

            items.Add(new OverviewItem(sentence, article));
        }

        return items.Count == 0 ? null : items;
    }
}
