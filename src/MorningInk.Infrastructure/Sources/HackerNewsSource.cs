using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Weather;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// Hacker News stories of the source's time window from its minimum points on, through Algolia's search: a link story
/// leads to the article itself, whose full text is fetched like any feed's; an "Ask HN" brings its own text.
/// </summary>
internal sealed class HackerNewsSource(SafeHttpClient http, IOptions<CommunityOptions> options, TimeProvider time) : ISource
{
    public const int DefaultMinScore = 100;

    private const string ItemUrl = "https://news.ycombinator.com/item?id=";
    private const int MaxHits = 50;

    public SourceKind Kind => SourceKind.HackerNews;

    public async Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var since = (now - source.MaxAge).ToUnixTimeSeconds();
        var min = source.MinScore ?? DefaultMinScore;
        var request = Urls.WithQuery($"{options.Value.HackerNewsApi.TrimEnd('/')}/search_by_date",
        [
            ("tags", "story"),
            ("numericFilters", string.Create(CultureInfo.InvariantCulture, $"points>={min},created_at_i>{since}")),
            ("hitsPerPage", MaxHits.ToString(CultureInfo.InvariantCulture)),
        ]);
        if (await http.GetAsync(request, cancellationToken: cancellationToken) is not FetchResult.Success page)
        {
            return new SourceResult.Failure("Hacker News did not answer.", Reason: "CommunityNoAnswer", Arguments: ["Hacker News"]);
        }

        try
        {
            using var json = JsonDocument.Parse(page.Content);
            if (!json.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
            {
                return new SourceResult.Failure("Hacker News returned no list of stories.", Reason: "CommunityNoList", Arguments: ["Hacker News"]);
            }

            var articles = hits.EnumerateArray().Select(ToArticle).OfType<FetchedArticle>();
            return new SourceResult.Success(SourceLimits.Apply(articles, source, now), ETag: null, LastModified: null);
        }
        catch (JsonException)
        {
            return new SourceResult.Failure("Hacker News returned no list of stories.", Reason: "CommunityNoList", Arguments: ["Hacker News"]);
        }
    }

    private static FetchedArticle? ToArticle(JsonElement hit)
    {
        var id = Json.Text(hit, "objectID");
        var title = Json.Text(hit, "title")?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(title))
        {
            return null;
        }

        // A story without a link is a text of its own ("Ask HN"), read on Hacker News itself.
        var text = Json.Text(hit, "story_text");
        var link = Json.Text(hit, "url");
        Uri? url;
        if (link is null)
        {
            url = new Uri(ItemUrl + Uri.EscapeDataString(id));
        }
        else if (!Uri.TryCreate(link, UriKind.Absolute, out url) || UrlPolicy.Check(url) is not null)
        {
            // Stories are untrusted: only web links, never "javascript:" or "file:".
            return null;
        }

        return new FetchedArticle(
            Id: $"hn:{id}",
            Title: title,
            Url: url,
            PublishedAt: Json.Number(hit, "created_at_i") is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            Author: Json.Votes(hit, "points", "num_comments"),
            Summary: null,
            Content: link is null ? text : null)
        {
            IsComplete = link is null && !string.IsNullOrWhiteSpace(text),
        };
    }
}
