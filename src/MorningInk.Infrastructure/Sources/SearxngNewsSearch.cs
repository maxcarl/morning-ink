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

    /// <summary>
    /// Searches once per term and merges the results: most engines behind SearXNG know neither OR nor quotes and answer
    /// such a query with nothing, while each term alone finds its news.
    /// </summary>
    /// <param name="baseUrl">The instance, e.g. "https://searx.example.com".</param>
    public async Task<SourceResult> SearchAsync(NewsQuery query, string baseUrl, CancellationToken cancellationToken)
    {
        var articles = new List<FetchedArticle>();
        var unresponsive = new List<string>();
        SourceResult.Failure? failure = null;
        var answered = false;
        foreach (var request in RequestUrls(query, baseUrl))
        {
            // One place name failing keeps what the others found; it only counts when none answered.
            var (found, failed, problem) = await SearchOneAsync(request, cancellationToken);
            if (problem is not null)
            {
                failure ??= problem;
                continue;
            }

            answered = true;
            articles.AddRange(found);
            unresponsive.AddRange(failed);
        }

        if (!answered && failure is not null)
        {
            return failure;
        }

        // Nothing found and engines failing, e.g. blocked by a CAPTCHA: that is the reason, not a quiet day.
        if (articles.Count == 0 && unresponsive.Count > 0)
        {
            var engines = string.Join(", ", unresponsive.Distinct());
            return new SourceResult.Failure($"SearXNG found nothing; these engines failed: {engines}.", Reason: "SearxngEnginesFailed", Arguments: [engines]);
        }

        return new SourceResult.Success(articles.DistinctBy(a => a.Url).ToList(), ETag: null, LastModified: null);
    }

    private async Task<(List<FetchedArticle> Found, List<string> Failed, SourceResult.Failure? Problem)> SearchOneAsync(Uri request, CancellationToken cancellationToken)
    {
        var fetched = await http.GetAsync(request, cancellationToken: cancellationToken);
        if (fetched is not FetchResult.Success page)
        {
            return ([], [], fetched switch
            {
                FetchResult.Failure { Error: FetchError.HttpError, Arguments: [_, Forbidden] } =>
                    new SourceResult.Failure("SearXNG refused the JSON format; add \"json\" to search.formats in its settings.yml.", Reason: "SearxngNoJson"),
                FetchResult.Failure failure => failure.ToSourceFailure(),
                _ => new SourceResult.Failure("The news search did not answer.", Reason: "NewsSearchNoAnswer"),
            });
        }

        return Read(page.Content) is var (found, failed)
            ? (found, failed, null)
            : ([], [], new SourceResult.Failure("The news search returned no readable answer.", Reason: "NewsSearchNoFeed"));
    }

    /// <summary>The searches as sent, one per term, so they can be tried in SearXNG's own page when nothing comes back.</summary>
    public IReadOnlyList<Uri> RequestUrls(NewsQuery query, string baseUrl)
    {
        var settings = options.Value;
        var country = query.Country ?? settings.Country;
        var language = Countries.LanguageOf(query.Country) ?? settings.Language;
        return query.Terms
            .Select(term => Urls.WithQuery(baseUrl.TrimEnd('/') + "/search",
            [
                ("q", term),
                ("categories", "news"),
                ("format", "json"),
                ("language", $"{language}-{country}"),
                ("time_range", TimeRange(query.MaxAge)),
            ]))
            .ToList();
    }

    // SearXNG knows day, week, month and year; the source's maximum age cuts off the rest afterwards.
    private static string TimeRange(TimeSpan maxAge) => maxAge.TotalDays switch
    {
        <= 1 => "day",
        <= 7 => "week",
        <= 31 => "month",
        _ => "year",
    };

    // The articles, and the engines that failed as "name (reason)", from [["bing news", "CAPTCHA"], …].
    private static (List<FetchedArticle> Articles, List<string> Unresponsive)? Read(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var unresponsive = root.TryGetProperty("unresponsive_engines", out var engines) && engines.ValueKind == JsonValueKind.Array
                ? engines.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2)
                    .Select(e => $"{e[0]} ({e[1]})")
                    .ToList()
                : [];
            return (results.EnumerateArray().Select(ToArticle).OfType<FetchedArticle>().ToList(), unresponsive);
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
