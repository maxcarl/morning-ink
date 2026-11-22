using System.Globalization;
using System.Xml.Linq;
using MorningInk.Application.Rendering;
using MorningInk.Application.Weather;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>Builds the XHTML pages of an issue. All text goes through <see cref="XElement"/>, so it is always escaped.</summary>
internal static class EpubPages
{
    public static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    public static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";

    public const string StyleSheet = "style.css";

    private const int WordsPerMinute = 200;
    private const int MaxHeadlines = 5;

    public static XDocument TitlePage(IssueContent issue, EpubTexts texts, Func<IssueArticle, string> hrefOf)
    {
        var headlines = issue.Sections.SelectMany(s => s.Articles.Take(1)).Take(MaxHeadlines).ToList();
        return Page(texts.TitlePage, texts,
            new XElement(Xhtml + "h1", issue.Title),
            new XElement(Xhtml + "p", new XAttribute("class", "dateline"), DateLine(issue, texts)),
            issue.Weather is null ? null : WeatherSection(issue.Weather, texts),
            issue.UnavailableSources.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", texts.Notes),
                new XElement(Xhtml + "ul", new XAttribute("class", "notes"), issue.UnavailableSources.Select(n => new XElement(Xhtml + "li", texts.SourceUnavailable(n))))),
            headlines.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", texts.Headlines),
                new XElement(Xhtml + "ul", headlines.Select(a => new XElement(Xhtml + "li",
                    new XElement(Xhtml + "a", new XAttribute("href", hrefOf(a)), a.Article.Title))))));
    }

    // Today in one line plus morning, midday and evening, then one line per following day.
    private static XElement WeatherSection(WeatherForecast forecast, EpubTexts texts)
    {
        var today = forecast.Days[0];
        var parts = forecast.Today.Select(p =>
            $"{texts.DayPart(p.Part)}: {Join(texts.Weather(p.WeatherCode), texts.Temperature(p.Temperature), Percent(p.PrecipitationProbability))}");
        return new XElement(Xhtml + "section", new XAttribute("class", "weather"),
            new XElement(Xhtml + "h2", texts.WeatherFor(forecast.Place)),
            new XElement(Xhtml + "p", Join(texts.Weather(today.WeatherCode), texts.TemperatureRange(today.MinTemperature, today.MaxTemperature), Precipitation(today.PrecipitationProbability, texts))),
            forecast.Today.Count == 0 ? null : new XElement(Xhtml + "p", string.Join(" · ", parts)),
            forecast.Days.Count < 2 ? null : new XElement(Xhtml + "ul", forecast.Days.Skip(1).Select(day => new XElement(Xhtml + "li",
                $"{day.Date.ToString("dddd", texts.Culture)}: {Join(texts.Weather(day.WeatherCode), texts.TemperatureRange(day.MinTemperature, day.MaxTemperature), Percent(day.PrecipitationProbability))}"))));
    }

    private static string? Precipitation(int? percent, EpubTexts texts) => percent is > 0 ? texts.Precipitation(percent.Value) : null;

    private static string? Percent(int? percent) => percent is > 0 ? $"{percent} %" : null;

    private static string Join(params string?[] parts) => string.Join(", ", parts.Where(p => p is not null));

    /// <summary>
    /// The page for a section's briefs: news that came as a headline only (local news, for example), gathered in one list
    /// with a link each, instead of a nearly empty page per headline. Full articles are not repeated here; the contents list them.
    /// </summary>
    public static XDocument BriefsPage(IssueSection section, IReadOnlyList<IssueArticle> briefs, DateTimeOffset issueTime, EpubTexts texts) =>
        Page(section.Name, texts,
            new XElement(Xhtml + "h1", section.Name),
            new XElement(Xhtml + "h2", texts.Briefs),
            new XElement(Xhtml + "ul", new XAttribute("class", "briefs"), briefs.Select(a => new XElement(Xhtml + "li",
                new XElement(Xhtml + "a", new XAttribute("href", ArticleBody.ToEpubUrl(a.Article.Url)), a.Article.Title),
                new XElement(Xhtml + "span", new XAttribute("class", "meta"), " · " + string.Join(" · ", new[]
                {
                    a.SourceName,
                    a.Article.PublishedAt?.ToOffset(issueTime.Offset).ToString("t", texts.Culture),
                }.Where(m => !string.IsNullOrWhiteSpace(m))))))));

    /// <summary>Whether an article has nothing to read but its headline, so it belongs among the briefs.</summary>
    public static bool IsBrief(ArticleBody.Result body) => body.Images.Count == 0 && ReadingWords(body.Nodes) == 0;

    /// <param name="section">Shown above the headline, like a newspaper's department line.</param>
    public static XDocument ArticlePage(IssueArticle item, string section, IReadOnlyList<XNode> body, DateTimeOffset issueTime, EpubTexts texts)
    {
        var article = item.Article;
        var meta = new[]
        {
            item.SourceName,
            article.PublishedAt?.ToOffset(issueTime.Offset).ToString("t", texts.Culture),
            article.Author,
        };
        return Page(article.Title, texts,
            new XElement(Xhtml + "article",
                new XElement(Xhtml + "p", new XAttribute("class", "kicker"), section),
                new XElement(Xhtml + "h1", article.Title),
                new XElement(Xhtml + "p", new XAttribute("class", "meta"), string.Join(" · ", meta.Where(m => !string.IsNullOrWhiteSpace(m)))),
                body,
                new XElement(Xhtml + "p", new XAttribute("class", "original"),
                    new XElement(Xhtml + "a", new XAttribute("href", ArticleBody.ToEpubUrl(article.Url)), texts.ReadOriginal))));
    }

    /// <summary>The EPUB 3 table of contents, also shown as a page for readers without their own TOC view.</summary>
    /// <param name="sectionHrefOf">Where a section starts: its first full article, or its briefs page.</param>
    /// <param name="articlesOf">The full articles of a section; briefs have no page of their own and no entry.</param>
    public static XDocument Navigation(IssueContent issue, EpubTexts texts, string titleHref, Func<int, string> sectionHrefOf, Func<int, IReadOnlyList<IssueArticle>> articlesOf, Func<IssueArticle, string> hrefOf) =>
        Page(texts.Contents, texts,
            new XElement(Xhtml + "nav", new XAttribute(Epub + "type", "toc"), new XAttribute("id", "toc"),
                new XElement(Xhtml + "h1", texts.Contents),
                new XElement(Xhtml + "ol",
                    new XElement(Xhtml + "li", new XElement(Xhtml + "a", new XAttribute("href", titleHref), texts.TitlePage)),
                    issue.Sections.Select((section, index) => new XElement(Xhtml + "li",
                        new XElement(Xhtml + "a", new XAttribute("href", sectionHrefOf(index)), section.Name),
                        articlesOf(index).Count == 0 ? null : new XElement(Xhtml + "ol", articlesOf(index).Select(a => new XElement(Xhtml + "li",
                            new XElement(Xhtml + "a", new XAttribute("href", hrefOf(a)), a.Article.Title)))))))));

    public static string DateLine(IssueContent issue, EpubTexts texts) =>
        $"{issue.PublishedAt.ToString("D", texts.Culture)} · {texts.IssueNumber(issue.Number)}";

    public static int ReadingMinutes(IReadOnlyList<XNode> body) =>
        Math.Max(1, (int)Math.Round((double)ReadingWords(body) / WordsPerMinute, MidpointRounding.AwayFromZero));

    private static int ReadingWords(IReadOnlyList<XNode> body) =>
        string.Concat(body.Select(n => n is XElement e ? e.Value + " " : (n as XText)?.Value))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static XDocument Page(string title, EpubTexts texts, params object?[] body) =>
        new(new XElement(Xhtml + "html",
            new XAttribute(XNamespace.Xmlns + "epub", Epub),
            new XAttribute(XNamespace.Xml + "lang", texts.Culture.Name),
            new XAttribute("lang", texts.Culture.Name),
            new XElement(Xhtml + "head",
                new XElement(Xhtml + "meta", new XAttribute("charset", "utf-8")),
                new XElement(Xhtml + "title", title),
                new XElement(Xhtml + "link", new XAttribute("rel", "stylesheet"), new XAttribute("type", "text/css"), new XAttribute("href", StyleSheet))),
            new XElement(Xhtml + "body", body)));

    // Plain and serif: e-readers apply their own fonts; images never wider than the screen.
    public const string Css = """
        body { font-family: serif; line-height: 1.4; margin: 0 0.5em; }
        h1 { font-size: 1.5em; margin: 0.5em 0; }
        h2 { font-size: 1.2em; }
        .dateline, .meta { color: #444; font-size: 0.9em; }
        .notes li { font-style: italic; }
        img { max-width: 100%; height: auto; }
        figure { margin: 1em 0; }
        figcaption { font-size: 0.85em; color: #444; }
        .original { margin-top: 1.5em; font-size: 0.9em; }
        .kicker { font-variant: small-caps; letter-spacing: 0.05em; color: #444; margin: 0; }
        .briefs li { margin-bottom: 0.8em; }
        """;

    public static string Iso(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
