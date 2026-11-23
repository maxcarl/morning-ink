using System.Text.Json;
using MorningInk.Application.Processing;
using MorningInk.Application.Rendering;

namespace MorningInk.Infrastructure.Ai;

/// <summary>
/// Neutral headlines: the AI gets every headline of the issue as numbered data, one request for all, and answers
/// with plain ones for those that lure instead of inform. The original stays with the article and the page shows it.
/// </summary>
/// <remarks>Off by default, since it changes what the publisher wrote; articles that must stay local never go to a cloud provider.</remarks>
internal sealed class AiHeadlineEditor(AiRequests ai) : IHeadlineEditor
{
    private const int MaxTitleCharacters = 200;
    private const int TeaserCharacters = 300;
    private const int MaxOutputTokens = 1500;

    /// <summary>The versioned default instructions, which the admin may replace on the settings page.</summary>
    public static readonly string DefaultPrompt = AiText.Prompt("headlines.txt");

    // Always appended, also to the admin's own instructions: the answer's form and the articles as data only.
    private static readonly string Format = AiText.Prompt("headlines-format.txt");

    public async Task<IReadOnlyList<Headline>?> RewriteAsync(IssueAiRequest request, CancellationToken cancellationToken)
    {
        if (await ai.SettingsAsync(cancellationToken) is not { NeutralHeadlines: true } settings)
        {
            return null;
        }

        var offered = request.Articles.Where(a => settings.IsLocal || !a.LocalAiOnly).Select(a => a.Article).ToList();
        if (offered.Count == 0)
        {
            return null;
        }

        var question = new AiQuestion(
            $"{settings.HeadlinesPrompt ?? DefaultPrompt}\n\n{Format}",
            $"<articles>\n{AiText.Numbered([.. offered.Select(a => a.Article)], TeaserCharacters)}\n</articles>",
            MaxOutputTokens,
            request.UserId,
            request.Edition,
            $"Headlines of {offered.Count} articles");
        return await ai.AskAsync(settings, question, answer => Checked(answer, offered), cancellationToken);
    }

    // Untrusted like every answer: each number an article offered and named once, each headline plain text of a
    // sensible length. One entry that does not check out and none is used: the model did not do what was asked.
    private static List<Headline>? Checked(string? answer, List<IssueArticle> offered)
    {
        if (AiText.Entries(answer, "titles") is not { } entries)
        {
            return null;
        }

        var headlines = new List<Headline>();
        var named = new HashSet<int>();
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("article", out var number) || AiText.Number(number, offered.Count) is not { } index
                || !entry.TryGetProperty("title", out var text) || text.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var title = AiText.Sentences(text.GetString());
            var article = offered[index - 1];
            if (title.Length is 0 or > MaxTitleCharacters || !named.Add(index))
            {
                return null;
            }

            // A "rewrite" that is the same headline changes nothing and would show the original twice.
            if (!string.Equals(title, AiText.Plain(article.Article.Title), StringComparison.Ordinal))
            {
                headlines.Add(new Headline(article, title));
            }
        }

        return headlines;
    }
}
