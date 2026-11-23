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
        if (!source.FetchFullText || article.IsComplete)
        {
            return article;
        }

        var fetched = await http.GetAsync(article.Url, cancellationToken: cancellationToken);
        if (fetched is not FetchResult.Success page || !IsHtml(page.MediaType))
        {
            LogFallback(logger, article.Url.Host, fetched is FetchResult.Failure failure ? failure.Message : "not an HTML page");
            return article;
        }

        if (IsGate(article.Url, page.FinalUri))
        {
            LogFallback(logger, article.Url.Host, "redirected to a consent, login or subscription page");
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

    // Consent, login and paywall pages send the reader back afterwards, so they carry the article's address in
    // their query ("?from=…", "?return=…"). Their text is readable, so the extractor alone would take it as the article.
    private static bool IsGate(Uri requested, Uri landed) =>
        requested.AbsolutePath.Length > 1 // "/" would appear in every query
        && landed.AbsolutePath != requested.AbsolutePath
        && Uri.UnescapeDataString(landed.Query).Contains(requested.AbsolutePath, StringComparison.Ordinal);

    // Servers without a content type get the benefit of the doubt; the extractor rejects non-articles anyway.
    private static bool IsHtml(string? mediaType) => mediaType is null or "text/html" or "application/xhtml+xml";

    [LoggerMessage(Level = LogLevel.Information, Message = "Keeping the feed text for an article from {Host}: {Reason}")]
    private static partial void LogFallback(ILogger logger, string host, string reason);
}
