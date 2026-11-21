using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Processing;

/// <summary>
/// The last pipeline step: cleans every text field, whatever earlier steps or the source produced.
/// Registered last on purpose; a test guards that order.
/// </summary>
internal sealed class SanitizingProcessor(ArticleSanitizer sanitizer) : IProcessor
{
    public Task<FetchedArticle> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken)
    {
        var title = sanitizer.Text(article.Title);
        return Task.FromResult(article with
        {
            Title = title.Length > 0 ? title : article.Url.ToString(),
            Author = article.Author is null ? null : sanitizer.Text(article.Author),
            Summary = article.Summary is null ? null : sanitizer.Html(article.Summary, article.Url),
            Content = article.Content is null ? null : sanitizer.Html(article.Content, article.Url),
        });
    }
}
