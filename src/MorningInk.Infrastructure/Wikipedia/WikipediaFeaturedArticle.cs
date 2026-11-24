using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningInk.Application.Wikipedia;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Wikipedia;

/// <summary>
/// Wikipedia's article of the day ("tfa" in the featured feed), from the Wikipedia in the language of the edition's
/// country. Not every Wikipedia picks one, so a missing one falls back to English.
/// </summary>
internal sealed partial class WikipediaFeaturedArticle(SafeHttpClient http, IOptions<WikipediaOptions> options, ILogger<WikipediaFeaturedArticle> logger) : IFeaturedArticle
{
    private const string Fallback = "en";
    private const int MaxTitleCharacters = 300;

    // The introduction of an article; far longer is not what this page is for.
    private const int MaxExtractCharacters = 4000;

    public async Task<FeaturedArticle?> ArticleAsync(DateOnly day, string? country, CancellationToken cancellationToken)
    {
        var language = Countries.LanguageOf(country) ?? Fallback;
        var article = await FetchAsync(language, day, cancellationToken);
        if (article is null && language != Fallback)
        {
            article = await FetchAsync(Fallback, day, cancellationToken);
        }

        if (article is null)
        {
            LogUnavailable(logger);
        }

        return article;
    }

    private async Task<FeaturedArticle?> FetchAsync(string language, DateOnly date, CancellationToken cancellationToken)
    {
        var url = new Uri(string.Create(CultureInfo.InvariantCulture, $"{options.Value.FeedUrl.TrimEnd('/')}/{Uri.EscapeDataString(language)}/featured/{date:yyyy}/{date:MM}/{date:dd}"));
        if (await http.GetAsync(url, cancellationToken: cancellationToken) is not FetchResult.Success response)
        {
            return null;
        }

        try
        {
            // The answer is untrusted: plain text of a sensible length and a web link only; the page escapes the text.
            // The HTML extract is never used.
            if (JsonSerializer.Deserialize<Response>(response.Content)?.Article is not { } tfa
                || tfa.Titles?.Normalized?.Trim() is not { Length: > 0 and <= MaxTitleCharacters } title
                || tfa.Extract?.Trim() is not { Length: > 0 and <= MaxExtractCharacters } extract
                || !Uri.TryCreate(tfa.Urls?.Desktop?.Page, UriKind.Absolute, out var page) || page.Scheme != Uri.UriSchemeHttps)
            {
                return null;
            }

            return new FeaturedArticle(title, [.. extract.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], page);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wikipedia's article of the day is unavailable; the issue comes without it")]
    private static partial void LogUnavailable(ILogger logger);

    private sealed record Response([property: JsonPropertyName("tfa")] Tfa? Article);

    private sealed record Tfa(
        [property: JsonPropertyName("titles")] Titles? Titles,
        [property: JsonPropertyName("extract")] string? Extract,
        [property: JsonPropertyName("content_urls")] ContentUrls? Urls);

    private sealed record Titles([property: JsonPropertyName("normalized")] string? Normalized);

    private sealed record ContentUrls([property: JsonPropertyName("desktop")] PageUrl? Desktop);

    private sealed record PageUrl([property: JsonPropertyName("page")] string? Page);
}
