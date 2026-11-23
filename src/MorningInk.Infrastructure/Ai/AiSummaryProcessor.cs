using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Ai;

/// <summary>
/// Adds a short AI summary to each article, last in the pipeline, after full text and cleaning. Optional in every way:
/// without settings, for a source that must stay local, or when the AI fails, the article stays as it is.
/// </summary>
/// <remarks>
/// One instance per issue run (scoped): after a failure or at the daily limit, the rest of the run does not ask again.
/// Every request is recorded in <see cref="AiUsage"/>, so the admin sees what went to which provider.
/// </remarks>
internal sealed partial class AiSummaryProcessor(AiSettingsStore store, AiChat chat, AiUsage usage, ILogger<AiSummaryProcessor> logger) : IProcessor
{
    private const int MinWords = 40;
    private const int MaxInputCharacters = 12_000;
    private const int MaxSummaryCharacters = 800;
    private const int MaxOutputTokens = 300;

    /// <summary>The versioned default instructions, which the admin may replace on the settings page.</summary>
    public static readonly string DefaultPrompt = AiText.Prompt("summarize.txt");

    // Always appended, also to the admin's own instructions: the article stays data, whatever the prompt says.
    private static readonly string Fence = AiText.Prompt("fence.txt");

    private AiSettings? _settings;

    public bool UsesAi => true;
    private bool _unavailable;

    public async Task<FetchedArticle> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken)
    {
        if (_unavailable)
        {
            return article;
        }

        _settings ??= await store.LoadAsync(cancellationToken);
        if (_settings is not { Enabled: true, Summaries: true, Endpoint: { } endpoint, Model: { } model } settings
            || (source.LocalAiOnly && !settings.IsLocal))
        {
            return article;
        }

        var text = AiText.Plain(article.Content ?? article.Summary);
        if (text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
        {
            return article;
        }

        if (await usage.LimitReachedAsync(settings.DailyLimit, cancellationToken))
        {
            _unavailable = true;
            LogLimit(logger);
            return article;
        }

        var input = text[..Math.Min(text.Length, MaxInputCharacters)];
        string? summary = null;
        try
        {
            // The article is data, fenced in and kept apart from the instructions; the answer is checked before use.
            ChatMessage[] messages =
            [
                new(ChatRole.System, $"{settings.SummaryPrompt ?? DefaultPrompt}\n\n{Fence}"),
                new(ChatRole.User, $"<article>\nTitle: {article.Title}\n\n{input}\n</article>"),
            ];
            var response = await chat.Create(settings).GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = MaxOutputTokens, Temperature = 0.2f }, cancellationToken);
            summary = Checked(response.Text);
            return summary is null ? article : article with { AiSummary = summary };
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            // Whatever goes wrong, the AI never costs an article: the publisher would leave out one that throws.
            _unavailable = true;
            LogUnavailable(logger, e.Message);
            return article;
        }
        finally
        {
            await usage.RecordAsync(new AiCall
            {
                At = usage.Now,
                Provider = AiUsage.ProviderOf(endpoint),
                Model = model,
                IsLocal = settings.IsLocal,
                UserId = source.UserId,
                SourceName = AiText.Shorten(source.Name, 200),
                ArticleTitle = AiText.Shorten(article.Title, 500),
                ArticleUrl = AiText.Shorten(article.Url.AbsoluteUri, 2000),
                Characters = input.Length,
                Succeeded = summary is not null,
            });
        }
    }

    // Plain sentences only, and an empty or overlong answer is no summary.
    private static string? Checked(string? answer)
    {
        var text = AiText.Sentences(answer);
        return text.Length is 0 or > MaxSummaryCharacters ? null : text;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI daily limit reached, the rest of this issue comes without summaries")]
    private static partial void LogLimit(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI not available, the rest of this issue comes without summaries: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
