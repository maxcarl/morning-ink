using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Ai;

/// <summary>
/// Adds a short AI summary to each article, last in the pipeline, after full text and cleaning. Optional in every way:
/// without settings, for a source that must stay local, or when the AI fails, the article stays as it is.
/// </summary>
/// <remarks>
/// Scoped like <see cref="AiRequests"/>: after a failure or at the daily limit, the rest of the run does not ask again.
/// The summary is written in the language of the source's owner, who reads it, not in the article's.
/// </remarks>
internal sealed class AiSummaryProcessor(AiRequests ai, MorningInkDbContext db) : IProcessor
{
    private const int MinWords = 40;
    private const int MaxInputCharacters = 12_000;
    private const int MaxSummaryCharacters = 800;
    private const int MaxOutputTokens = 300;

    /// <summary>The versioned default instructions, which the admin may replace on the settings page.</summary>
    public static readonly string DefaultPrompt = AiText.Prompt("summarize.txt");

    // Always appended, also to the admin's own instructions: the article stays data, whatever the prompt says.
    private static readonly string Fence = AiText.Prompt("fence.txt");

    // One lookup per owner and run: an issue summarizes many articles of the same user.
    private readonly Dictionary<Guid, string> _languages = [];

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
        var instructions = $"{settings.SummaryPrompt ?? DefaultPrompt}\n\n{Fence}"
            .Replace("{language}", AiText.LanguageName(await LanguageAsync(source.UserId, cancellationToken)), StringComparison.Ordinal);
        var question = new AiQuestion(
            instructions,
            $"<article>\nTitle: {article.Title}\n\n{AiText.Shorten(text, MaxInputCharacters)}\n</article>",
            MaxOutputTokens,
            source.UserId,
            source.Name,
            article.Title,
            article.Url.AbsoluteUri);
        return await ai.AskAsync(settings, question, Checked, cancellationToken) is { } summary ? article with { AiSummary = summary } : article;
    }

    private async Task<string> LanguageAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!_languages.TryGetValue(userId, out var language))
        {
            // An owner that cannot be found gets English, like an unknown culture.
            language = await db.DomainUsers.Where(u => u.Id == userId).Select(u => u.Language).SingleOrDefaultAsync(cancellationToken) ?? "en";
            _languages[userId] = language;
        }

        return language;
    }

    // Plain sentences only, and an empty or overlong answer is no summary.
    private static string? Checked(string? answer)
    {
        var text = AiText.Sentences(answer);
        return text.Length is 0 or > MaxSummaryCharacters ? null : text;
    }
}
