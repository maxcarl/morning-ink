using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// The day's top posts of a subreddit from the source's minimum score on. A text post brings its own text; a link post
/// leads to the article, whose full text is fetched like any feed's. Pinned posts, videos and NSFW posts are left out.
/// </summary>
internal sealed partial class RedditSource(SafeHttpClient http, IOptions<CommunityOptions> options, TimeProvider time) : ISource
{
    public const int DefaultMinScore = 100;

    private const string PostSite = "https://www.reddit.com";
    private const int Limit = 50;

    public SourceKind Kind => SourceKind.Reddit;

    /// <summary>"selfhosted" from "r/selfhosted", "/r/selfhosted/" or the subreddit's address; null when it is none.</summary>
    public static string? Subreddit(string written)
    {
        var name = written.Trim().TrimEnd('/');
        var slash = name.LastIndexOf("r/", StringComparison.OrdinalIgnoreCase);
        name = slash >= 0 ? name[(slash + 2)..] : name;
        return SubredditName().IsMatch(name) ? name : null;
    }

    public async Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        if (source.Url is null || Subreddit(source.Url) is not { } subreddit)
        {
            return new SourceResult.Failure("This Reddit source names no subreddit.", Reason: "SubredditInvalid");
        }

        var request = new Uri($"{options.Value.RedditUrl.TrimEnd('/')}/r/{subreddit}/top.json?t=day&limit={Limit}");
        if (await http.GetAsync(request, cancellationToken: cancellationToken) is not FetchResult.Success page)
        {
            return new SourceResult.Failure("Reddit did not answer.", Reason: "CommunityNoAnswer", Arguments: ["Reddit"]);
        }

        try
        {
            using var json = JsonDocument.Parse(page.Content);
            if (!json.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
            {
                return new SourceResult.Failure("Reddit returned no list of posts.", Reason: "CommunityNoList", Arguments: ["Reddit"]);
            }

            var min = source.MinScore ?? DefaultMinScore;
            var articles = children.EnumerateArray()
                .Select(child => child.TryGetProperty("data", out var post) ? post : default)
                .Where(post => Json.Number(post, "score") >= min && !Json.Flag(post, "stickied") && !Json.Flag(post, "is_video") && !Json.Flag(post, "over_18"))
                .Select(ToArticle)
                .OfType<FetchedArticle>();
            return new SourceResult.Success(SourceLimits.Apply(articles, source, time.GetUtcNow()), ETag: null, LastModified: null);
        }
        catch (JsonException)
        {
            return new SourceResult.Failure("Reddit returned no list of posts.", Reason: "CommunityNoList", Arguments: ["Reddit"]);
        }
    }

    private static FetchedArticle? ToArticle(JsonElement post)
    {
        var title = Json.Text(post, "title")?.Trim();
        var self = Json.Flag(post, "is_self");

        // A text post is read on Reddit itself; a link post leads to what it links. Only web links either way.
        var link = self ? PostSite + Json.Text(post, "permalink") : Json.Text(post, "url");
        if (string.IsNullOrEmpty(title) || !Uri.TryCreate(link, UriKind.Absolute, out var url) || UrlPolicy.Check(url) is not null)
        {
            return null;
        }

        // Reddit sends the post's HTML escaped once more; cleaned later like every article.
        var text = Json.Text(post, "selftext_html") is { } escaped ? WebUtility.HtmlDecode(escaped) : null;
        return new FetchedArticle(
            Id: Json.Text(post, "id") is { } id ? $"t3_{id}" : null,
            Title: title,
            Url: url,
            PublishedAt: Json.Number(post, "created_utc") is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            Author: Json.Votes(post, "score", "num_comments"),
            Summary: null,
            Content: self ? text : null)
        {
            IsComplete = self && !string.IsNullOrWhiteSpace(text),
        };
    }

    // Reddit's rule for names: letters, digits and underscores, 2 to 21 of them.
    [GeneratedRegex("^[A-Za-z0-9_]{2,21}$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex SubredditName();
}
