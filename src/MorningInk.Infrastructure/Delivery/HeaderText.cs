using System.Text.RegularExpressions;

namespace MorningInk.Infrastructure.Delivery;

/// <summary>Makes untrusted text safe for mail headers and attachment names.</summary>
internal static partial class HeaderText
{
    private const int MaxFileNameLength = 100;
    private const string FallbackName = "issue";

    private static readonly char[] UnsafeFileNameChars = [.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>One line without control characters: a line break can never start a new header (header injection).</summary>
    public static string SingleLine(string text) => Spaces().Replace(ControlCharacters().Replace(text, " "), " ").Trim();

    /// <summary>A file name without paths or control characters, e.g. "Morning Ink 2026-11-21.epub".</summary>
    public static string FileName(string title, string extension)
    {
        var name = SingleLine(title);
        foreach (var c in UnsafeFileNameChars)
        {
            name = name.Replace(c, '_');
        }

        // No leading dots (hidden files, "..") and no trailing dots or spaces (not allowed on Windows).
        name = name.TrimStart('.').TrimEnd('.', ' ');
        if (name.Length > MaxFileNameLength)
        {
            name = name[..MaxFileNameLength].TrimEnd();
        }

        return (name.Length == 0 ? FallbackName : name) + extension;
    }

    [GeneratedRegex(@"\p{C}")]
    private static partial Regex ControlCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
