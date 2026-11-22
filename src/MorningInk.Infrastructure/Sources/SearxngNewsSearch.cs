using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Weather;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// News search via a self-hosted SearXNG, used instead of Bing when an address is set (see <see cref="NewsSearchSettingsStore"/>):
/// the towns searched for stay with one's own instance, which asks its news engines. Its results link to the publishers,
/// so the full text comes from there like with any feed.
/// </summary>
/// <remarks>Needs "json" among <c>search.formats</c> in SearXNG's settings.yml; without it SearXNG answers 403.</remarks>
public sealed class SearxngNewsSearch(SafeHttpClient http, IOptions<NewsSearchOptions> options)
{
    private const string Forbidden = "403";

    /// <param name="baseUrl">The instance, e.g. "https://searx.example.com".</param>
    public async Task<SourceResult> SearchAsync(NewsQuery query, string baseUrl, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var country = query.Country ?? settings.Country;
        var language = Countries.LanguageOf(query.Country) ?? settings.Language;
        var terms = string.Join(" OR ", query.Terms.Select(t => $"\"{t.Replace("\"", string.Empty, StringComparison.Ordinal)}\""));
        (string, string)[] parameters =
        [
            ("q", terms),
            ("categories", "news"),
            ("format", "json"),
            ("language", $"{language}-{country}"),
            ("time_range", TimeRange(query.MaxAge)),
        ];

        var endpoint = baseUrl.TrimEnd('/') + "/search";
        var fetched = await http.GetAsync(Urls.WithQuery(endpoint, parameters), cancellationToken: cancellationToken);
        if (fetched is not FetchResult.Success page)
        {
            return fetched switch
            {
                FetchResult.Failure { Error: FetchError.HttpError, Arguments: [_, Forbidden] } =>
                    new SourceResult.Failure("SearXNG refused the JSON format; add \"json\" to search.formats in its settings.yml.", Reason: "SearxngNoJson"),
                FetchResult.Failure failure => failure.ToSourceFailure(),
                _ => new SourceResult.Failure("The news search did not answer.", Reason: "NewsSearchNoAnswer"),
            };
        }

        if (Read(page.Content) is not { } articles)
        {
            return new SourceResult.Failure("The news search returned no readable answer.", Reason: "NewsSearchNoFeed");
        }

        return new SourceResult.Success(articles, ETag: null, LastModified: null);
    }

    // SearXNG knows day, week, month and year; the source's maximum age cuts off the rest afterwards.
    private static string TimeRange(TimeSpan maxAge) => maxAge.TotalDays switch
    {
        <= 1 => "day",
        <= 7 => "week",
        <= 31 => "month",
        _ => "year",
    };

    private static List<FetchedArticle>? Read(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return results.EnumerateArray().Select(ToArticle).OfType<FetchedArticle>().ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FetchedArticle? ToArticle(JsonElement result)
    {
        var title = Text(result, "title")?.Trim();
        var link = Text(result, "url");

        // Results are untrusted: only web links to an article, never "javascript:" or "file:".
        if (string.IsNullOrEmpty(title) || !Uri.TryCreate(link, UriKind.Absolute, out var url) || UrlPolicy.Check(url) is not null)
        {
            return null;
        }

        // The teaser is plain text, escaped so the HTML cleaning never takes a "<" in it for a tag.
        var teaser = Text(result, "content")?.Trim();
        return new FetchedArticle(
            Id: null,
            Title: title,
            Url: url,
            PublishedAt: Date(Text(result, "publishedDate")),
            Author: null,
            Summary: string.IsNullOrEmpty(teaser) ? null : $"<p>{System.Security.SecurityElement.Escape(teaser)}</p>",
            Content: null);
    }

    private static string? Text(JsonElement result, string name) =>
        result.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // ISO dates, with or without offset; without one, SearXNG means UTC.
    private static DateTimeOffset? Date(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}
