using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MorningInk.Application.Sources;

namespace MorningInk.Infrastructure.Ai;

/// <summary>Text going to and coming from the AI: prompts from the repository, articles as plain text, answers checked.</summary>
internal static partial class AiText
{
    /// <summary>A prompt kept versioned in the repository, under Ai/Prompts.</summary>
    public static string Prompt(string name)
    {
        using var stream = typeof(AiText).Assembly.GetManifestResourceStream($"MorningInk.Infrastructure.Ai.Prompts.{name}")
            ?? throw new InvalidOperationException($"Prompt {name} is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    /// <summary>Tags removed, entities left encoded: "&amp;lt;/article&amp;gt;" in a text cannot close the fence around it.</summary>
    public static string Plain(string? html) => Whitespace().Replace(Tags().Replace(html ?? string.Empty, " "), " ").Trim();

    /// <summary>An answer as plain sentences: whatever markup the model added is dropped.</summary>
    public static string Sentences(string? answer) => Whitespace().Replace(Tags().Replace(answer ?? string.Empty, string.Empty), " ").Trim();

    public static string Shorten(string text, int length) => text.Length <= length ? text : text[..length];

    /// <summary>"[1] Title\nTeaser" per article: the number is all an answer may point to.</summary>
    public static string Numbered(IReadOnlyList<FetchedArticle> articles, int teaserCharacters)
    {
        var text = new StringBuilder();
        for (var i = 0; i < articles.Count; i++)
        {
            var article = articles[i];
            var teaser = Plain(article.AiSummary ?? article.Summary ?? article.Content);
            text.Append(CultureInfo.InvariantCulture, $"[{i + 1}] {Plain(article.Title)}\n{Shorten(teaser, teaserCharacters)}\n\n");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// The entries of an answer in the form {"<paramref name="list"/>":[{...}]}; null when the answer is no JSON of
    /// that form. A code fence around it is taken off, since models like to add one.
    /// </summary>
    public static IReadOnlyList<JsonElement>? Entries(string? answer, string list) =>
        Object(answer) is { } json && json.TryGetProperty(list, out var entries) && entries.ValueKind == JsonValueKind.Array
            ? [.. entries.EnumerateArray()]
            : null;

    /// <summary>The answer as a JSON object; null when it is none. A code fence around it is taken off.</summary>
    public static JsonElement? Object(string? answer)
    {
        var json = (answer ?? string.Empty).Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            json = json.Trim('`').Trim();
            json = json.StartsWith("json", StringComparison.OrdinalIgnoreCase) ? json[4..] : json;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A number from 1 to <paramref name="count"/>, as the data was numbered; null for anything else.</summary>
    public static int? Number(JsonElement value, int count) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 1 && number <= count ? number : null;

    /// <summary>
    /// Whether the model repeated <paramref name="title"/>, so the number it gave is the article it meant. Models drop
    /// or change quotes, dashes and capitals when they repeat a headline; letters and digits must match.
    /// </summary>
    public static bool SameHeadline(string? repeated, string title) =>
        LettersAndDigits(Plain(repeated)) is { Length: > 0 } letters && letters == LettersAndDigits(Plain(title));

    private static string LettersAndDigits(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>The model writes in the reader's language, named in English: "German", not "Deutsch" or "de".</summary>
    public static string LanguageName(string culture)
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

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Whitespace();
}
