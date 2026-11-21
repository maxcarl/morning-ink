using System.Globalization;
using System.Xml.Linq;
using MorningInk.Application.Rendering;

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
            issue.UnavailableSources.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", texts.Notes),
                new XElement(Xhtml + "ul", new XAttribute("class", "notes"), issue.UnavailableSources.Select(n => new XElement(Xhtml + "li", texts.SourceUnavailable(n))))),
            headlines.Count == 0 ? null : new XElement(Xhtml + "section",
                new XElement(Xhtml + "h2", texts.Headlines),
                new XElement(Xhtml + "ul", headlines.Select(a => new XElement(Xhtml + "li",
                    new XElement(Xhtml + "a", new XAttribute("href", hrefOf(a)), a.Article.Title))))));
    }

    public static XDocument SectionPage(IssueSection section, EpubTexts texts, Func<IssueArticle, string> hrefOf, Func<IssueArticle, int> minutesOf) =>
        Page(section.Name, texts,
            new XElement(Xhtml + "h1", section.Name),
            new XElement(Xhtml + "ul", new XAttribute("class", "articles"), section.Articles.Select(a => new XElement(Xhtml + "li",
                new XElement(Xhtml + "a", new XAttribute("href", hrefOf(a)), a.Article.Title),
                new XElement(Xhtml + "span", new XAttribute("class", "meta"), $" · {a.SourceName} · {texts.ReadingTime(minutesOf(a))}")))));

    public static XDocument ArticlePage(IssueArticle item, IReadOnlyList<XNode> body, DateTimeOffset issueTime, EpubTexts texts)
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
                new XElement(Xhtml + "h1", article.Title),
                new XElement(Xhtml + "p", new XAttribute("class", "meta"), string.Join(" · ", meta.Where(m => !string.IsNullOrWhiteSpace(m)))),
                body,
                new XElement(Xhtml + "p", new XAttribute("class", "original"),
                    new XElement(Xhtml + "a", new XAttribute("href", ArticleBody.ToEpubUrl(article.Url)), texts.ReadOriginal))));
    }

    /// <summary>The EPUB 3 table of contents, also shown as a page for readers without their own TOC view.</summary>
    public static XDocument Navigation(IssueContent issue, EpubTexts texts, string titleHref, Func<int, string> sectionHrefOf, Func<IssueArticle, string> hrefOf) =>
        Page(texts.Contents, texts,
            new XElement(Xhtml + "nav", new XAttribute(Epub + "type", "toc"), new XAttribute("id", "toc"),
                new XElement(Xhtml + "h1", texts.Contents),
                new XElement(Xhtml + "ol",
                    new XElement(Xhtml + "li", new XElement(Xhtml + "a", new XAttribute("href", titleHref), texts.TitlePage)),
                    issue.Sections.Select((section, index) => new XElement(Xhtml + "li",
                        new XElement(Xhtml + "a", new XAttribute("href", sectionHrefOf(index)), section.Name),
                        new XElement(Xhtml + "ol", section.Articles.Select(a => new XElement(Xhtml + "li",
                            new XElement(Xhtml + "a", new XAttribute("href", hrefOf(a)), a.Article.Title)))))))));

    public static string DateLine(IssueContent issue, EpubTexts texts) =>
        $"{issue.PublishedAt.ToString("D", texts.Culture)} · {texts.IssueNumber(issue.Number)}";

    public static int ReadingMinutes(IReadOnlyList<XNode> body)
    {
        var words = string.Concat(body.Select(n => n is XElement e ? e.Value + " " : (n as XText)?.Value))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Round((double)words / WordsPerMinute, MidpointRounding.AwayFromZero));
    }

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
        """;

    public static string Iso(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
