using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MorningInk.Application.Processing;
using MorningInk.Application.Rendering;
using MorningInk.Domain;

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
internal sealed partial class AiOverviewWriter(AiSettingsStore store, AiChat chat, AiUsage usage, ILogger<AiOverviewWriter> logger) : IIssueOverview
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

    public async Task<IReadOnlyList<OverviewItem>?> WriteAsync(OverviewRequest request, CancellationToken cancellationToken)
    {
        var settings = await store.LoadAsync(cancellationToken);
        if (settings is not { Enabled: true, Endpoint: { } endpoint, Model: { } model })
        {
            return null;
        }

        var offered = request.Articles.Where(a => settings.IsLocal || !a.LocalAiOnly).Select(a => a.Article).ToList();
        if (offered.Count == 0 || await usage.LimitReachedAsync(settings.DailyLimit, cancellationToken))
        {
            return null;
        }

        var data = Numbered(offered);
        IReadOnlyList<OverviewItem>? items = null;
        try
        {
            ChatMessage[] messages =
            [
                new(ChatRole.System, $"{settings.OverviewPrompt ?? DefaultPrompt}\n\n{Format}"
                    .Replace("{count}", $"{Math.Min(MinItems, offered.Count)} to {Math.Min(MaxItems, offered.Count)}", StringComparison.Ordinal)
                    .Replace("{language}", LanguageName(request.Language), StringComparison.Ordinal)),
                new(ChatRole.User, $"<articles>\n{data}\n</articles>"),
            ];
            var response = await chat.Create(settings).GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = MaxOutputTokens, Temperature = 0.2f }, cancellationToken);
            items = Checked(response.Text, offered);
            return items;
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            LogUnavailable(logger, e.Message);
            return null;
        }
        finally
        {
            await usage.RecordAsync(new AiCall
            {
                At = usage.Now,
                Provider = AiUsage.ProviderOf(endpoint),
                Model = model,
                IsLocal = settings.IsLocal,
                UserId = request.UserId,
                SourceName = AiText.Shorten(request.Edition, 200),
                ArticleTitle = AiText.Shorten($"Overview of {offered.Count} articles", 500),
                ArticleUrl = string.Empty,
                Characters = data.Length,
                Succeeded = items is not null,
            });
        }
    }

    // "[1] Title\nTeaser": the number is all the answer may point to.
    private static string Numbered(List<IssueArticle> articles)
    {
        var text = new StringBuilder();
        for (var i = 0; i < articles.Count; i++)
        {
            var article = articles[i].Article;
            var teaser = AiText.Plain(article.AiSummary ?? article.Summary ?? article.Content);
            text.Append(CultureInfo.InvariantCulture, $"[{i + 1}] {AiText.Plain(article.Title)}\n{AiText.Shorten(teaser, TeaserCharacters)}\n\n");
        }

        return text.ToString().TrimEnd();
    }

    // The answer is untrusted: valid JSON in the asked form, each number an article offered and used once, each text
    // a plain sentence; anything else is no overview.
    private static List<OverviewItem>? Checked(string? answer, List<IssueArticle> offered)
    {
        var json = (answer ?? string.Empty).Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            // Models like to wrap JSON in a code fence, with or without "json" after it.
            json = json.Trim('`').Trim();
            json = json.StartsWith("json", StringComparison.OrdinalIgnoreCase) ? json[4..] : json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("items", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = new List<OverviewItem>();
            foreach (var entry in list.EnumerateArray().Take(MaxItems))
            {
                if (!entry.TryGetProperty("article", out var number) || !number.TryGetInt32(out var index) || index < 1 || index > offered.Count
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
        catch (JsonException)
        {
            return null;
        }
    }

    // The model writes in the reader's language, named in English: "German", not "Deutsch" or "de".
    private static string LanguageName(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return "English";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI not available, the issue comes without its overview: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, string reason);
}
