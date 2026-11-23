using System.Text.RegularExpressions;

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

    [GeneratedRegex("<[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex Whitespace();
}
