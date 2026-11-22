using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MorningInk.Infrastructure.Persistence;

/// <summary>Stores a list of small objects as one JSON column, for data that is always read with its owner.</summary>
internal static class JsonColumn
{
    public static ValueConverter<List<T>, string> Converter<T>() =>
        new(v => Serialize(v), v => Deserialize<T>(v));

    // Compared by content, so changing an item inside the list is saved too.
    public static ValueComparer<List<T>> Comparer<T>() =>
        new((a, b) => Serialize(a) == Serialize(b), v => Serialize(v).GetHashCode(StringComparison.Ordinal), v => Deserialize<T>(Serialize(v)));

    private static string Serialize<T>(List<T>? value) => JsonSerializer.Serialize(value ?? []);

    private static List<T> Deserialize<T>(string text) => JsonSerializer.Deserialize<List<T>>(text) ?? [];
}
