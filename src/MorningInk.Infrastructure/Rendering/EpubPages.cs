using System.Globalization;
using System.Xml.Linq;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
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
            issue.WeatherOnTitlePage && issue.Weather is { } forecast ? WeatherSection(forecast, texts, texts.WeatherFor(forecast.Place)) : null,
            issue.UnavailableSources.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", texts.Notes),
                new XElement(Xhtml + "ul", new XAttribute("class", "notes"), issue.UnavailableSources.Select(n => new XElement(Xhtml + "li", texts.SourceUnavailable(n))))),
            headlines.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", texts.Headlines),
                new XElement(Xhtml + "ul", headlines.Select(a => new XElement(Xhtml + "li",
                    new XElement(Xhtml + "a", new XAttribute("href", hrefOf(a)), a.Article.Title))))));
    }

    /// <summary>The weather on a page of its own, where the edition placed it further down.</summary>
    public static XDocument WeatherPage(string title, WeatherForecast forecast, EpubTexts texts) =>
        Page(title, texts,
            new XElement(Xhtml + "h1", title),
            WeatherSection(forecast, texts, texts.WeatherFor(forecast.Place)));

    /// <summary>"The most important in 5 minutes": a sentence per main news, each leading to its article.</summary>
    public static XDocument OverviewPage(string title, IReadOnlyList<OverviewItem> items, EpubTexts texts, Func<IssueArticle, string> hrefOf) =>
        Page(title, texts,
            new XElement(Xhtml + "h1", title),
            new XElement(Xhtml + "ol", new XAttribute("class", "overview"), items.Select(item => new XElement(Xhtml + "li",
                new XElement(Xhtml + "a", new XAttribute("href", hrefOf(item.Article)), item.Text)))));

    // Today in one line plus morning, midday and evening, then one line per following day.
    private static XElement WeatherSection(WeatherForecast forecast, EpubTexts texts, string heading)
    {
        var today = forecast.Days[0];
        var parts = forecast.Today.Select(p =>
            $"{texts.DayPart(p.Part)}: {Join(texts.Weather(p.WeatherCode), texts.Temperature(p.Temperature), Percent(p.PrecipitationProbability))}");
        return new XElement(Xhtml + "section", new XAttribute("class", "weather"),
            new XElement(Xhtml + "h2", heading),
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
                // A brief links to its web page; a newsletter's "mid:" address cannot be opened, so its title stays text.
                IsWebPage(a.Article.Url)
                    ? new XElement(Xhtml + "a", new XAttribute("href", ArticleBody.ToEpubUrl(a.Article.Url)), a.Article.Title)
                    : new XText(a.Article.Title),
                new XElement(Xhtml + "span", new XAttribute("class", "meta"), " · " + string.Join(" · ", new[]
                {
                    a.SourceName,
                    a.Article.PublishedAt?.ToOffset(issueTime.Offset).ToString("t", texts.Culture),
                    a.AlsoAt.Count == 0 ? null : texts.AlsoAt(string.Join(", ", a.AlsoAt.Select(o => o.SourceName).Distinct())),
                }.Where(m => !string.IsNullOrWhiteSpace(m))))))));

    // The AI's summary as a box above the text; added as text, so whatever it says can never become markup.
    private static XElement? Summary(FetchedArticle article, EpubTexts texts) => string.IsNullOrWhiteSpace(article.AiSummary)
        ? null
        : new XElement(Xhtml + "aside", new XAttribute("class", "summary"),
            new XElement(Xhtml + "p", new XAttribute("class", "kicker"), texts.InShort),
            new XElement(Xhtml + "p", article.AiSummary));

    private static bool IsWebPage(Uri url) => url.Scheme is "http" or "https";

    private static XElement BackToContents(string contentsHref, EpubTexts texts) =>
        new(Xhtml + "p", new XAttribute("class", "back"), new XElement(Xhtml + "a", new XAttribute("href", contentsHref), texts.BackToContents));

    /// <summary>The id of a page's entry in the contents, e.g. "toc-article-1-2", to link right to it.</summary>
    public static string ContentsId(string href) => $"toc-{Path.GetFileNameWithoutExtension(href)}";

    // The same story from other sources, merged into this one.
    private static XElement? AlsoAt(IssueArticle item, EpubTexts texts) => item.AlsoAt.Count == 0
        ? null
        : new XElement(Xhtml + "p", new XAttribute("class", "meta"), texts.AlsoAt(string.Join(", ", item.AlsoAt.Select(a => a.SourceName).Distinct())));

    /// <summary>Whether an article has nothing to read but its headline, so it belongs among the briefs.</summary>
    public static bool IsBrief(ArticleBody.Result body) => body.Images.Count == 0 && ReadingWords(body.Nodes) == 0;

    /// <param name="section">Shown above the headline, like a newspaper's department line.</param>
    /// <param name="contentsHref">The article's own entry in the contents, to go back to and on with the next one.</param>
    /// <param name="feedback">"More of this" and "less of this"; null when readers cannot reach the instance.</param>
    public static XDocument ArticlePage(IssueArticle item, string section, IReadOnlyList<XNode> body, DateTimeOffset issueTime, string contentsHref, FeedbackLinks? feedback, EpubTexts texts)
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
                AlsoAt(item, texts),
                Summary(article, texts),

                // For a reader whom the summary told enough: straight back to the contents, at this article.
                string.IsNullOrWhiteSpace(article.AiSummary) ? null : BackToContents(contentsHref, texts),
                body,

                // Only a web page can be read in the original; a newsletter's "mid:" address cannot be opened. The way back
                // to the contents is there for every article.
                new XElement(Xhtml + "p", new XAttribute("class", "original"),
                    IsWebPage(article.Url) ? new XElement(Xhtml + "a", new XAttribute("href", ArticleBody.ToEpubUrl(article.Url)), texts.ReadOriginal) : null,
                    IsWebPage(article.Url) ? " · " : null,
                    new XElement(Xhtml + "a", new XAttribute("href", contentsHref), texts.BackToContents)),

                // The reader's answer goes to the instance in the e-reader's browser; the edition learns from it.
                feedback is null ? null : new XElement(Xhtml + "p", new XAttribute("class", "feedback"),
                    new XElement(Xhtml + "a", new XAttribute("href", feedback.More.AbsoluteUri), texts.FeedbackMore),
                    " · ",
                    new XElement(Xhtml + "a", new XAttribute("href", feedback.Less.AbsoluteUri), texts.FeedbackLess))));
    }

    /// <summary>The EPUB 3 table of contents, also shown as a page for readers without their own TOC view.</summary>
    /// <param name="toc">Entries in reading order, the same as the NCX gets; a section lists its full articles.</param>
    public static XDocument Navigation(IReadOnlyList<EpubPackage.TocEntry> toc, EpubTexts texts) =>
        Page(texts.Contents, texts,
            new XElement(Xhtml + "nav", new XAttribute(Epub + "type", "toc"), new XAttribute("id", "toc"),
                new XElement(Xhtml + "h1", texts.Contents),
                Entries(toc)));

    // Only entries without children get an id: a section shares its href with its first article, and ids must be unique.
    private static XElement Entries(IReadOnlyList<EpubPackage.TocEntry> entries) =>
        new(Xhtml + "ol", entries.Select(entry => new XElement(Xhtml + "li",
            entry.Children.Count == 0 ? new XAttribute("id", ContentsId(entry.Href)) : null,
            new XElement(Xhtml + "a", new XAttribute("href", entry.Href), entry.Label),
            entry.Children.Count == 0 ? null : Entries(entry.Children))));

    /// <summary>What the cover shows: the first story of the first section as the lead, then the sections' stories in turn.</summary>
    public static CoverPage Cover(IssueContent issue, EpubTexts texts)
    {
        // Round robin over the sections, so the cover shows the breadth of the issue, not just its first section.
        var stories = Enumerable.Range(0, issue.Sections.Max(s => s.Articles.Count))
            .SelectMany(i => issue.Sections.Where(s => s.Articles.Count > i).Select(s => new CoverStory(s.Name, s.Articles[i].Article.Title)))
            .ToList();
        var weather = issue.Weather is { Days.Count: > 0 } forecast
            ? $"{texts.WeatherFor(forecast.Place)}: {Join(texts.Weather(forecast.Days[0].WeatherCode), texts.TemperatureRange(forecast.Days[0].MinTemperature, forecast.Days[0].MaxTemperature))}"
            : null;
        var articles = issue.Sections.Sum(s => s.Articles.Count);
        return new CoverPage(
            issue.Title,
            issue.PublishedAt.ToString("D", texts.Culture),
            texts.IssueNumber(issue.Number),
            stories[0],
            stories.Skip(1).ToList(),
            weather,
            texts.ArticleCount(articles, issue.Sections.Count));
    }

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
        .back { margin: 0.5em 0 1.5em; font-size: 0.9em; }
        .feedback { margin-top: 0.5em; font-size: 0.9em; }
        .kicker { font-variant: small-caps; letter-spacing: 0.05em; color: #444; margin: 0; }
        .briefs li { margin-bottom: 0.8em; }
        .summary { border-top: 1px solid #444; border-bottom: 1px solid #444; margin: 0.8em 0; padding: 0.3em 0; font-style: italic; }
        """;

    public static string Iso(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
