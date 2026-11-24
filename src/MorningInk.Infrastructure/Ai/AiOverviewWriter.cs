using System.Globalization;
using System.Text.Json;
using MorningInk.Application.Processing;
using MorningInk.Application.Rendering;

namespace MorningInk.Infrastructure.Ai;

/// <summary>
/// Writes the issue's overview, "the most important in 5 minutes": the AI gets every chosen article as numbered data
/// and answers with a paragraph about the main news and the numbers of their articles, each with its headline
/// repeated. Only what checks out is used: a plain paragraph of a sensible length, articles in the issue whose
/// headline matches their number.
/// </summary>
/// <remarks>
/// Optional like every AI step: without settings, at the daily limit or when the AI fails, there is no overview and
/// the issue comes without it. Articles that must stay local never go to a cloud provider.
/// </remarks>
internal sealed class AiOverviewWriter(AiRequests ai) : IIssueOverview
{
    private const int MinItems = 5;
    private const int MaxItems = 8;

    // A paragraph of up to about 250 words: what can be read in a few minutes, with room for long words.
    private const int MaxSummaryCharacters = 2000;
    private const int TeaserCharacters = 400;
    private const int MaxOutputTokens = 1500;

    /// <summary>The versioned default instructions, which the admin may replace on the settings page.</summary>
    public static readonly string DefaultPrompt = AiText.Prompt("overview.txt");

    // Always appended, also to the admin's own instructions: the answer's form the check relies on, and the articles
    // as data only.
    private static readonly string Format = AiText.Prompt("overview-format.txt");

    public async Task<IssueOverview?> WriteAsync(IssueAiRequest request, CancellationToken cancellationToken)
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

    // The answer is untrusted: a plain paragraph, each number an article offered and used once with its own headline
    // repeated. A headline that is not the numbered one means the model mixed up the numbers, so a link could lead
    // to the wrong story: anything that does not check out is no overview.
    private static IssueOverview? Checked(string? answer, List<IssueArticle> offered)
    {
        if (AiText.Object(answer) is not { } json
            || !json.TryGetProperty("summary", out var summaryValue) || summaryValue.ValueKind != JsonValueKind.String
            || AiText.Sentences(summaryValue.GetString()) is not { Length: > 0 and <= MaxSummaryCharacters } summary
            || AiText.Entries(answer, "items") is not { } entries)
        {
            return null;
        }

        var articles = new List<IssueArticle>();
        foreach (var entry in entries.Take(MaxItems))
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("article", out var number) || AiText.Number(number, offered.Count) is not { } index
                || !entry.TryGetProperty("original", out var original) || original.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var article = offered[index - 1];
            if (!AiText.SameHeadline(original.GetString(), article.Article.Title) || articles.Any(a => ReferenceEquals(a, article)))
            {
                return null;
            }

            articles.Add(article);
        }

        return articles.Count == 0 ? null : new IssueOverview(summary, articles);
    }
}
