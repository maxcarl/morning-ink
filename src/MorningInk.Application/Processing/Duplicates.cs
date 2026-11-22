using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Processing;

/// <summary>Tells whether two articles report the same story, so an issue carries it once.</summary>
/// <remarks>
/// The same address (without tracking parameters) always counts. Otherwise the headlines decide: most meaningful words
/// of the shorter one must appear in the other. That catches cut-off and reordered headlines; stories that are only
/// told in different words stay apart, which costs a repeat but never loses a story.
/// </remarks>
public static class Duplicates
{
    private const int MinWordLength = 3;
    private const int MinSharedWords = 3;
    private const double MinShare = 0.6;

    // Short words that say nothing about the story, in the languages issues come in.
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "und", "der", "die", "das", "den", "dem", "des", "ein", "eine", "einer", "einem", "mit", "von", "für", "auf",
        "aus", "bei", "nach", "über", "wie", "ist", "sind", "wird", "the", "and", "for", "with", "from", "after", "over",
    };

    public static bool AreSame(FetchedArticle first, FetchedArticle second)
    {
        if (UrlNormalizer.Normalize(first.Url) == UrlNormalizer.Normalize(second.Url))
        {
            return true;
        }

        var a = Words(first.Title);
        var b = Words(second.Title);
        var shared = a.Count(b.Contains);
        return shared >= MinSharedWords && shared >= MinShare * Math.Min(a.Count, b.Count);
    }

    private static HashSet<string> Words(string title) =>
        new string(title.ToLowerInvariant().Replace("ß", "ss", StringComparison.Ordinal).Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= MinWordLength && !Filler.Contains(word))
            .ToHashSet(StringComparer.Ordinal);
}
