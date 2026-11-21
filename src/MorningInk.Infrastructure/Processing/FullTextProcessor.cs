using Microsoft.Extensions.Logging;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Processing;

/// <summary>Replaces the feed's teaser with the full article from the web page, if the page has one.</summary>
internal sealed partial class FullTextProcessor(SafeHttpClient http, ILogger<FullTextProcessor> logger) : IProcessor
{
    public async Task<FetchedArticle> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken)
    {
        // ponytail: Google News links only lead to Google, which reveals the article with JavaScript; with a news
        // search that returns real article URLs (SearXNG) local news can get full text too.
        if (!source.FetchFullText || source.Kind == SourceKind.Local)
        {
            return article;
        }

        var fetched = await http.GetAsync(article.Url, cancellationToken: cancellationToken);
        if (fetched is not FetchResult.Success page || !IsHtml(page.MediaType))
        {
            LogFallback(logger, article.Url.Host, fetched is FetchResult.Failure failure ? failure.Message : "not an HTML page");
            return article;
        }

        var content = ArticleExtractor.Extract(page.Content, page.FinalUri);
        if (content is null)
        {
            LogFallback(logger, article.Url.Host, "no article found on the page");
            return article;
        }

        return article with { Content = content };
    }

    // Servers without a content type get the benefit of the doubt; the extractor rejects non-articles anyway.
    private static bool IsHtml(string? mediaType) => mediaType is null or "text/html" or "application/xhtml+xml";

    [LoggerMessage(Level = LogLevel.Information, Message = "Keeping the feed text for an article from {Host}: {Reason}")]
    private static partial void LogFallback(ILogger logger, string host, string reason);
}
