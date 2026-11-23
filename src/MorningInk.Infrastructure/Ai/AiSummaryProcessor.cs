using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Ai;

/// <summary>
/// Adds a short AI summary to each article, last in the pipeline, after full text and cleaning. Optional in every way:
/// without settings, for a source that must stay local, or when the AI fails, the article stays as it is.
/// </summary>
/// <remarks>
/// Scoped like <see cref="AiRequests"/>: after a failure or at the daily limit, the rest of the run does not ask again.
/// </remarks>
internal sealed class AiSummaryProcessor(AiRequests ai) : IProcessor
{
    private const int MinWords = 40;
    private const int MaxInputCharacters = 12_000;
    private const int MaxSummaryCharacters = 800;
    private const int MaxOutputTokens = 300;

    /// <summary>The versioned default instructions, which the admin may replace on the settings page.</summary>
    public static readonly string DefaultPrompt = AiText.Prompt("summarize.txt");

    // Always appended, also to the admin's own instructions: the article stays data, whatever the prompt says.
    private static readonly string Fence = AiText.Prompt("fence.txt");

    public bool UsesAi => true;

    public async Task<FetchedArticle> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken)
    {
        if (await ai.SettingsAsync(cancellationToken) is not { Summaries: true } settings || (source.LocalAiOnly && !settings.IsLocal))
        {
            return article;
        }

        var text = AiText.Plain(article.Content ?? article.Summary);
        if (text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
        {
            return article;
        }

        // The article is data, fenced in and kept apart from the instructions; the answer is checked before use.
        var question = new AiQuestion(
            $"{settings.SummaryPrompt ?? DefaultPrompt}\n\n{Fence}",
            $"<article>\nTitle: {article.Title}\n\n{AiText.Shorten(text, MaxInputCharacters)}\n</article>",
            MaxOutputTokens,
            source.UserId,
            source.Name,
            article.Title,
            article.Url.AbsoluteUri);
        return await ai.AskAsync(settings, question, Checked, cancellationToken) is { } summary ? article with { AiSummary = summary } : article;
    }

    // Plain sentences only, and an empty or overlong answer is no summary.
    private static string? Checked(string? answer)
    {
        var text = AiText.Sentences(answer);
        return text.Length is 0 or > MaxSummaryCharacters ? null : text;
    }
}
