using Microsoft.Extensions.Localization;
using MorningInk.Domain;

namespace MorningInk.Web;

/// <summary>Why a source failed, in the user's language; see <see cref="ReasonTexts"/>.</summary>
public static class SourceTexts
{
    private const int FirstValue = 3;

    public static string Describe(IStringLocalizer<SharedResource> texts, string? reason, IReadOnlyList<string> arguments, string fallback) =>
        ReasonTexts.Describe(texts, "Source", reason, arguments, fallback);

    /// <summary>"heise: heise.de ist nicht erreichbar." from a "SourceFailed" log entry.</summary>
    /// <remarks>The entry holds source, English text, code and values; entries written before the codes have only the first two.</remarks>
    public static string Problem(IStringLocalizer<SharedResource> texts, IssueLogEntry entry)
    {
        var arguments = entry.Arguments;
        var source = arguments.ElementAtOrDefault(0) ?? string.Empty;
        var message = arguments.ElementAtOrDefault(1) ?? string.Empty;
        var reason = arguments.ElementAtOrDefault(2) is { Length: > 0 } code ? code : null;
        return $"{source}: {Describe(texts, reason, [.. arguments.Skip(FirstValue)], message)}";
    }
}
