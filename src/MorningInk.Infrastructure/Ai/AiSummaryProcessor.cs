using System.Text.RegularExpressions;
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
/// <remarks>One instance per issue run (scoped): after a failure, the rest of the run does not wait for the AI again.</remarks>
internal sealed partial class AiSummaryProcessor(AiSettingsStore store, AiChat chat, ILogger<AiSummaryProcessor> logger) : IProcessor
{
    private const int MinWords = 40;
    private const int MaxInputCharacters = 12_000;
    private const int MaxSummaryCharacters = 800;
    private const int MaxOutputTokens = 300;

    private static readonly string Instructions = LoadPrompt("summarize.txt");

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
        if (_settings is not { Enabled: true, Summaries: true, Endpoint: not null, Model: not null } settings
            || (source.LocalAiOnly && !settings.IsLocal))
        {
            return article;
        }

        var text = PlainText(article.Content ?? article.Summary);
        if (text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
        {
            return article;
        }

        try
        {
            // The article is data, fenced in and kept apart from the instructions; the answer is checked before use.
            ChatMessage[] messages =
            [
                new(ChatRole.System, Instructions),
                new(ChatRole.User, $"<article>\nTitle: {article.Title}\n\n{text[..Math.Min(text.Length, MaxInputCharacters)]}\n</article>"),
            ];
            var response = await chat.Create(settings).GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = MaxOutputTokens, Temperature = 0.2f }, cancellationToken);
            return Checked(response.Text) is { } summary ? article with { AiSummary = summary } : article;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            // Whatever goes wrong, the AI never costs an article: the publisher would leave out one that throws.
            _unavailable = true;
            LogUnavailable(logger, e.Message);
            return article;
        }
    }

    // Plain sentences only: markup is removed, and an empty or overlong answer is no summary.
    private static string? Checked(string? answer)
    {
        var text = Whitespace().Replace(Tags().Replace(answer ?? string.Empty, string.Empty), " ").Trim();
        return text.Length is 0 or > MaxSummaryCharacters ? null : text;
    }

    // Tags removed, entities left encoded: "&lt;/article&gt;" in a text cannot close the fence around it.
    private static string PlainText(string? html) => Whitespace().Replace(Tags().Replace(html ?? string.Empty, " "), " ").Trim();

    private static string LoadPrompt(string name)
    {
        using var stream = typeof(AiSummaryProcessor).Assembly.GetManifestResourceStream($"MorningInk.Infrastructure.Ai.Prompts.{name}")
            ?? throw new InvalidOperationException($"Prompt {name} is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Whitespace();

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI not available, the rest of this issue comes without summaries: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
