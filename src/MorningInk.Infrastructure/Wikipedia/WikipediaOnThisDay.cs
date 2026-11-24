using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningInk.Application.Wikipedia;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Wikipedia;

/// <summary>
/// Wikipedia's editors' pick of a day's events ("selected"), from the Wikipedia in the language of the edition's
/// country. Not every Wikipedia has the list, so a missing one falls back to English.
/// </summary>
internal sealed partial class WikipediaOnThisDay(SafeHttpClient http, IOptions<WikipediaOptions> options, ILogger<WikipediaOnThisDay> logger) : IOnThisDay
{
    private const string Fallback = "en";
    private const int MaxEvents = 5;

    // A selected event is a sentence or two; anything far longer is not what this page is for.
    private const int MaxTextCharacters = 600;

    public async Task<IReadOnlyList<Anniversary>> EventsAsync(DateOnly day, string? country, CancellationToken cancellationToken)
    {
        var language = Countries.LanguageOf(country) ?? Fallback;
        var events = await FetchAsync(language, day, cancellationToken);
        if (events.Count == 0 && language != Fallback)
        {
            events = await FetchAsync(Fallback, day, cancellationToken);
        }

        if (events.Count == 0)
        {
            LogUnavailable(logger);
        }

        return events;
    }

    private async Task<IReadOnlyList<Anniversary>> FetchAsync(string language, DateOnly date, CancellationToken cancellationToken)
    {
        var url = new Uri(string.Create(CultureInfo.InvariantCulture, $"{options.Value.FeedUrl.TrimEnd('/')}/{Uri.EscapeDataString(language)}/onthisday/selected/{date:MM}/{date:dd}"));
        if (await http.GetAsync(url, cancellationToken: cancellationToken) is not FetchResult.Success response)
        {
            return [];
        }

        try
        {
            // The answer is untrusted: only plain text of a sensible length is kept; the page escapes it.
            return JsonSerializer.Deserialize<Response>(response.Content)?.Selected is { } selected
                ? [.. selected
                    .Select(e => new Anniversary(e.Year, e.Text?.Trim() ?? string.Empty))
                    .Where(e => e.Text.Length is > 0 and <= MaxTextCharacters)
                    .Take(MaxEvents)
                    .OrderBy(e => e.Year)]
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wikipedia's \"On this day\" is unavailable; the issue comes without it")]
    private static partial void LogUnavailable(ILogger logger);

    private sealed record Response([property: JsonPropertyName("selected")] Event[]? Selected);

    private sealed record Event(
        [property: JsonPropertyName("year")] int Year,
        [property: JsonPropertyName("text")] string? Text);
}
