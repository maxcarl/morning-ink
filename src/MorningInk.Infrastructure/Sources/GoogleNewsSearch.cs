using System.Globalization;
using Microsoft.Extensions.Options;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Weather;

namespace MorningInk.Infrastructure.Sources;

/// <summary>News search via the Google News RSS feed. Results are headlines with their publisher; the links lead through Google.</summary>
internal sealed class GoogleNewsSearch(SafeHttpClient http, IOptions<NewsSearchOptions> options) : INewsSearch
{
    private const string TitleSeparator = " - ";

    public async Task<SourceResult> SearchAsync(NewsQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var country = query.Country ?? settings.Country;
        var language = Countries.LanguageOf(query.Country) ?? settings.Language;

        // Quoted phrases joined with OR; "when:" is Google's own recency filter, in whole days.
        var days = Math.Max(1, (int)Math.Ceiling(query.MaxAge.TotalDays));
        var terms = string.Join(" OR ", query.Terms.Select(t => $"\"{t.Replace("\"", string.Empty, StringComparison.Ordinal)}\""));
        (string, string)[] parameters =
        [
            ("q", string.Create(CultureInfo.InvariantCulture, $"{terms} when:{days}d")),
            ("hl", language),
            ("gl", country),
            ("ceid", $"{country}:{language}"),
        ];

        var fetched = await http.GetAsync(Urls.WithQuery(settings.GoogleNewsUrl, parameters), cancellationToken: cancellationToken);
        if (fetched is not FetchResult.Success page)
        {
            return new SourceResult.Failure(fetched is FetchResult.Failure failure ? failure.Message : "The news search did not answer.");
        }

        return FeedParser.Parse(page.Content, page.FinalUri) is SourceResult.Success success
            ? success with { Articles = success.Articles.Select(Clean).ToList() }
            : new SourceResult.Failure("The news search returned no readable feed.");
    }

    // Google appends " - Publisher" to every title and puts only a list of links into the description:
    // the publisher becomes the author, and the useless description goes.
    private static FetchedArticle Clean(FetchedArticle article)
    {
        var separator = article.Title.LastIndexOf(TitleSeparator, StringComparison.Ordinal);
        return separator <= 0
            ? article with { Summary = null, Content = null }
            : article with
            {
                Title = article.Title[..separator],
                Author = article.Title[(separator + TitleSeparator.Length)..],
                Summary = null,
                Content = null,
            };
    }
}
