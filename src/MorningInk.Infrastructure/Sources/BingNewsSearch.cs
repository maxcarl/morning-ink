using System.Xml.Linq;
using Microsoft.Extensions.Options;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Weather;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// News search via Bing News RSS. Unlike Google News, its links carry the publisher's address and each result has a
/// teaser, so local news can be read in full like any feed (see ADR 0003). The search terms, i.e. towns, go to Microsoft.
/// </summary>
/// <remarks>The RSS output is not an official API; should it change, the source reports a failure and the issue appears without it.</remarks>
internal sealed class BingNewsSearch(SafeHttpClient http, IOptions<NewsSearchOptions> options) : INewsSearch
{
    // Bing names its own elements with the request URL as namespace, so they are found by local name.
    private const string SourceElement = "Source";
    private const string TargetParameter = "url";

    public async Task<SourceResult> SearchAsync(NewsQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var country = query.Country ?? settings.Country;
        var language = Countries.LanguageOf(query.Country) ?? settings.Language;
        var terms = string.Join(" OR ", query.Terms.Select(t => $"\"{t.Replace("\"", string.Empty, StringComparison.Ordinal)}\""));
        (string, string)[] parameters =
        [
            ("q", terms),
            ("format", "rss"),
            ("setlang", language),
            ("cc", country),

            // Newest first; the source's maximum age then cuts off what is too old.
            ("qft", "sortbydate=\"1\""),
        ];

        var fetched = await http.GetAsync(Urls.WithQuery(settings.BingNewsUrl, parameters), cancellationToken: cancellationToken);
        if (fetched is not FetchResult.Success page)
        {
            return fetched is FetchResult.Failure failure
                ? failure.ToSourceFailure()
                : new SourceResult.Failure("The news search did not answer.", Reason: "NewsSearchNoAnswer");
        }

        if (FeedParser.LoadXml(page.Content)?.Root?.Element("channel") is not { } channel)
        {
            return new SourceResult.Failure("The news search returned no readable feed.", Reason: "NewsSearchNoFeed");
        }

        var articles = channel.Elements("item").Select(ToArticle).OfType<FetchedArticle>().ToList();
        return new SourceResult.Success(articles, ETag: null, LastModified: null);
    }

    private static FetchedArticle? ToArticle(XElement item)
    {
        var title = item.Element("title")?.Value.Trim();
        if (string.IsNullOrEmpty(title) || PublisherUrl(item.Element("link")?.Value) is not { } url)
        {
            return null;
        }

        // The teaser is plain text; escaped (only <, >, &, quotes, umlauts stay), so the HTML cleaning never takes a "<" in it for a tag.
        var teaser = item.Element("description")?.Value.Trim();
        return new FetchedArticle(
            Id: null,
            Title: title,
            Url: url,
            PublishedAt: FeedParser.ParseDate(item.Element("pubDate")?.Value),
            Author: item.Elements().FirstOrDefault(e => e.Name.LocalName == SourceElement)?.Value.Trim(),
            Summary: string.IsNullOrEmpty(teaser) ? null : $"<p>{System.Security.SecurityElement.Escape(teaser)}</p>",
            Content: null);
    }

    // Bing links go through a click counter whose "url" parameter is the article itself. Taken from there, the article
    // is fetched straight from the publisher; any link that is not http(s) is dropped.
    private static Uri? PublisherUrl(string? link)
    {
        if (!Uri.TryCreate(link?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var target = System.Web.HttpUtility.ParseQueryString(uri.Query)[TargetParameter];
        var candidate = target is not null && Uri.TryCreate(target, UriKind.Absolute, out var direct) ? direct : uri;
        return candidate.Scheme is "http" or "https" ? candidate : null;
    }
}
