using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
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
    public static readonly string DefaultPrompt = LoadPrompt("summarize.txt").Trim();

    // Always appended, also to the admin's own instructions: the article stays data, whatever the prompt says.
    private static readonly string Fence = LoadPrompt("fence.txt").Trim();

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

        var text = PlainText(article.Content ?? article.Summary);
        if (text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
        {
            return article;
        }

        if (settings.DailyLimit is { } limit && await usage.Last24HoursAsync(cancellationToken) >= limit)
        {
            _unavailable = true;
            LogLimit(logger, limit);
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
            // Failed calls count too: the text left the instance either way.
            await RecordAsync(new AiCall
            {
                At = usage.Now,
                Provider = Provider(endpoint),
                Model = model,
                IsLocal = settings.IsLocal,
                UserId = source.UserId,
                SourceName = Shorten(source.Name, 200),
                ArticleTitle = Shorten(article.Title, 500),
                ArticleUrl = Shorten(article.Url.AbsoluteUri, 2000),
                Characters = input.Length,
                Succeeded = summary is not null,
            });
        }
    }

    private async Task RecordAsync(AiCall call)
    {
        try
        {
            // Recorded even when the run is cancelled right after the call: the text has left the instance.
            await usage.RecordAsync(call, CancellationToken.None);
        }
        catch (Exception e) when (e is DbUpdateException or InvalidOperationException)
        {
            // The record is for transparency; losing one must not cost the summary.
            LogNotRecorded(logger, e.Message);
        }
    }

    // "192.168.1.20:11434": where the text went, without path or key.
    private static string Provider(string endpoint)
    {
        var uri = new Uri(endpoint);
        return $"{uri.Host}:{uri.Port}";
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length];

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI daily limit of {Limit} requests reached, the rest of this issue comes without summaries")]
    private static partial void LogLimit(ILogger logger, int limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI request could not be recorded: {Reason}")]
    private static partial void LogNotRecorded(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI not available, the rest of this issue comes without summaries: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
