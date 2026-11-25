using System.Globalization;
using System.Xml.Linq;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
using MorningInk.Application.Wikipedia;

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
        var headlines = InTurn(issue).Take(MaxHeadlines).Select(s => s.Article).ToList();
        return Page(texts.TitlePage, texts,
            new XElement(Xhtml + "h1", issue.Title),
            new XElement(Xhtml + "p", new XAttribute("class", "dateline"), DateLine(issue, texts)),
            issue.UnavailableSources.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", new XAttribute("class", "rubric"), texts.Notes),
                new XElement(Xhtml + "ul", new XAttribute("class", "notes"), issue.UnavailableSources.Select(n => new XElement(Xhtml + "li", texts.SourceUnavailable(n))))),
            headlines.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", new XAttribute("class", "rubric"), texts.Headlines),
                new XElement(Xhtml + "ul", new XAttribute("class", "index"), headlines.Select(a => new XElement(Xhtml + "li",
                    new XElement(Xhtml + "a", new XAttribute("href", hrefOf(a)), a.Article.Title))))));
    }

    /// <summary>The weather on a page of its own, where the edition placed it further down.</summary>
    public static XDocument WeatherPage(string title, WeatherForecast forecast, EpubTexts texts) =>
        Page(title, texts,
            new XElement(Xhtml + "h1", title),
            WeatherSection(forecast, texts, texts.WeatherFor(forecast.Place)));

    /// <summary>"The most important in 5 minutes": a paragraph about the main news, then their articles to read on.</summary>
    public static XDocument OverviewPage(string title, IssueOverview overview, EpubTexts texts, Func<IssueArticle, string> hrefOf) =>
        Page(title, texts,
            new XElement(Xhtml + "h1", title),
            new XElement(Xhtml + "p", new XAttribute("class", "overview"), overview.Summary),
            new XElement(Xhtml + "h2", new XAttribute("class", "rubric"), texts.ReadOn),
            new XElement(Xhtml + "ul", new XAttribute("class", "index"), overview.Articles.Select(article => new XElement(Xhtml + "li",
                new XElement(Xhtml + "a", new XAttribute("href", hrefOf(article)), article.Article.Title)))));

    /// <summary>
    /// "On this day": the day's anniversaries with their year and Wikipedia's article of the day, each when present,
    /// and the credit Wikipedia's licence asks for.
    /// </summary>
    public static XDocument OnThisDayPage(string title, IReadOnlyList<Anniversary>? events, FeaturedArticle? featured, DateTimeOffset issueTime, EpubTexts texts) =>
        Page(title, texts,
            new XElement(Xhtml + "h1", title),
            new XElement(Xhtml + "p", new XAttribute("class", "dateline"), issueTime.ToString("M", texts.Culture)),
            events is { Count: > 0 }
                ? new XElement(Xhtml + "ul", new XAttribute("class", "anniversaries"), events.Select(e => new XElement(Xhtml + "li",
                    new XElement(Xhtml + "strong", e.Year.ToString(texts.Culture)), " – ", e.Text)))
                : null,
            featured is null ? null : FeaturedSection(featured, texts),
            new XElement(Xhtml + "p", new XAttribute("class", "meta"), texts.FromWikipedia));

    private static IEnumerable<XElement> FeaturedSection(FeaturedArticle featured, EpubTexts texts) =>
    [
        new XElement(Xhtml + "h2", texts.FeaturedArticle),
        new XElement(Xhtml + "h3", featured.Title),
        .. featured.Paragraphs.Select(p => new XElement(Xhtml + "p", p)),
        new XElement(Xhtml + "p", new XElement(Xhtml + "a", new XAttribute("href", ArticleBody.ToEpubUrl(featured.Url)), texts.ReadOnWikipedia)),
    ];

    // Set like a newspaper's weather box: today's range large, then a line each for morning, midday and evening and for
    // the following days. No tables: Kindle marks every table with a zoom icon.
    private static XElement WeatherSection(WeatherForecast forecast, EpubTexts texts, string heading)
    {
        var today = forecast.Days[0];
        return new XElement(Xhtml + "section", new XAttribute("class", "weather"),
            new XElement(Xhtml + "h2", new XAttribute("class", "rubric"), heading),
            new XElement(Xhtml + "p", new XAttribute("class", "today"),
                new XElement(Xhtml + "span", new XAttribute("class", "temperature"), texts.TemperatureRange(today.MinTemperature, today.MaxTemperature)),
                Join(texts.Weather(today.WeatherCode), Precipitation(today.PrecipitationProbability, texts))),
            forecast.Today.Count == 0 ? null : new XElement(Xhtml + "ul", new XAttribute("class", "day-parts"),
                forecast.Today.Select(p => WeatherLine(texts.DayPart(p.Part), texts.Temperature(p.Temperature), texts.Weather(p.WeatherCode), p.PrecipitationProbability))),
            forecast.Days.Count < 2 ? null : new XElement(Xhtml + "ul", new XAttribute("class", "days"),
                forecast.Days.Skip(1).Select(day => WeatherLine(day.Date.ToString("dddd", texts.Culture), texts.TemperatureRange(day.MinTemperature, day.MaxTemperature), texts.Weather(day.WeatherCode), day.PrecipitationProbability))));
    }

    // "Morgens 1 °C Bedeckt, 20 %": the time in small caps, the temperature bold, so the eye finds both down the list.
    private static XElement WeatherLine(string when, string temperature, string weather, int? precipitation) =>
        new(Xhtml + "li",
            // The space inside the span: one standing alone between two elements is lost when the page is read back.
            new XElement(Xhtml + "span", new XAttribute("class", "when"), when + " "),
            new XElement(Xhtml + "span", new XAttribute("class", "temperature"), temperature),
            " " + Join(weather, Percent(precipitation)));

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
    /// <remarks>An AI summary is something to read: an overlong article shortened to it keeps its page.</remarks>
    public static bool IsBrief(IssueArticle article, ArticleBody.Result body) =>
        string.IsNullOrWhiteSpace(article.Article.AiSummary) && body.Images.Count == 0 && ReadingWords(body.Nodes) == 0;

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

                // The AI rewrote a lurid headline: what the publisher wrote stays visible, small, under the plain one.
                article.OriginalTitle is null ? null : new XElement(Xhtml + "p", new XAttribute("class", "original-title"), texts.OriginalTitle(article.OriginalTitle)),
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
    // A section is marked, so the page can set it like a newspaper's section heading.
    private static XElement Entries(IReadOnlyList<EpubPackage.TocEntry> entries) =>
        new(Xhtml + "ol", entries.Select(entry => new XElement(Xhtml + "li",
            entry.Children.Count == 0 ? new XAttribute("id", ContentsId(entry.Href)) : new XAttribute("class", "section"),
            new XElement(Xhtml + "a", new XAttribute("href", entry.Href), entry.Label),
            entry.Children.Count == 0 ? null : Entries(entry.Children))));

    // Round robin over the sections, so cover and headlines show the breadth of the issue, not just its first section.
    private static IEnumerable<(IssueSection Section, IssueArticle Article)> InTurn(IssueContent issue) =>
        Enumerable.Range(0, issue.Sections.Select(s => s.Articles.Count).DefaultIfEmpty().Max())
            .SelectMany(i => issue.Sections.Where(s => s.Articles.Count > i).Select(s => (s, s.Articles[i])));

    /// <summary>What the cover shows: the first story of the first section as the lead, then the sections' stories in turn.</summary>
    public static CoverPage Cover(IssueContent issue, EpubTexts texts)
    {
        var stories = InTurn(issue).Select(s => new CoverStory(s.Section.Name, s.Article.Article.Title)).ToList();
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
        .original-title { margin: -0.3em 0 0.5em; font-size: 0.85em; font-style: italic; color: #444; }
        .back { margin: 0.5em 0 1.5em; font-size: 0.9em; }
        .feedback { margin-top: 0.5em; font-size: 0.9em; }
        .kicker { font-variant: small-caps; letter-spacing: 0.05em; color: #444; margin: 0; }
        .briefs li { margin-bottom: 0.8em; }
        .summary { border-top: 1px solid #444; border-bottom: 1px solid #444; margin: 0.8em 0; padding: 0.3em 0; font-style: italic; }
        #toc ol, ul.index { list-style: none; margin: 0; padding: 0; }
        #toc li, ul.index li { margin: 0.4em 0; }
        #toc a, ul.index a { color: inherit; text-decoration: none; }
        #toc li.section { margin-top: 1.4em; }
        #toc li.section > a, .rubric { display: block; font-size: 1em; font-variant: small-caps; letter-spacing: 0.08em; font-weight: bold; border-bottom: 1px solid #444; padding-bottom: 0.15em; margin-bottom: 0.5em; }
        .rubric { margin-top: 1.4em; }
        #toc li li, ul.index li { padding-left: 1em; text-indent: -1em; }
        .weather .today { margin: 0.3em 0 1em; }
        .weather .today .temperature { display: block; font-size: 2em; font-weight: bold; line-height: 1.2; }
        .weather ul { list-style: none; margin: 0 0 1em; padding: 0; }
        .weather li { border-top: 1px solid #999; padding: 0.3em 0; }
        .weather li .when { font-variant: small-caps; letter-spacing: 0.05em; color: #444; }
        .weather li .temperature { font-weight: bold; }
        """;

    public static string Iso(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
