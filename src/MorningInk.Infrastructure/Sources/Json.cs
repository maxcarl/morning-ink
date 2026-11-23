using System.Globalization;
using System.Text.Json;

namespace MorningInk.Infrastructure.Sources;

/// <summary>Reading untrusted JSON from sources: a value is only taken when it has the expected type.</summary>
internal static class Json
{
    /// <summary>The property as text; null when it is missing, not a string, or the element is no object.</summary>
    public static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static long? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? (long)number
            : null;

    public static bool Flag(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>"412 points, 128 comments": what the community made of it, shown where the author would stand.</summary>
    public static string Votes(JsonElement element, string score, string comments) =>
        string.Create(CultureInfo.InvariantCulture, $"{Number(element, score) ?? 0} points, {Number(element, comments) ?? 0} comments");
}
