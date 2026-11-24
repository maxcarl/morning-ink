using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
using MorningInk.Application.Wikipedia;
using MorningInk.Infrastructure.Processing;
using MorningInk.Infrastructure.Tests.Processing;
using SkiaSharp;

namespace MorningInk.Infrastructure.Tests.Rendering;

/// <summary>
/// Structure checks for the EPUB. The full validation runs in CI with epubcheck on the file this test
/// writes to <c>MORNINGINK_EPUB_OUTPUT</c>.
/// </summary>
public sealed class EpubRendererTests : IDisposable
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("morning-ink-").FullName;
    private readonly string _imageName;

    public EpubRendererTests()
    {
        var jpeg = Assert.IsType<ImageConversion.Converted>(ImageConverter.Convert(TestImages.Png(800, 600), new ImageOptions())).Jpeg;
        _imageName = Convert.ToHexStringLower(SHA256.HashData(jpeg)) + ".jpg";
        Directory.CreateDirectory(Path.Combine(_dataDirectory, "images"));
        File.WriteAllBytes(Path.Combine(_dataDirectory, "images", _imageName), jpeg);
    }

    [Fact]
    public async Task Mimetype_comes_first_and_uncompressed()
    {
        using var epub = await RenderAsync();

        var first = epub.Entries[0];
        Assert.Equal("mimetype", first.FullName);
        Assert.Equal(first.Length, first.CompressedLength);
        Assert.Equal("application/epub+zip", ReadText(first));
    }

    [Fact]
    public async Task Package_has_unique_title_metadata_and_every_listed_file()
    {
        using var epub = await RenderAsync();

        var rootfile = (string)XDocument.Parse(ReadText(epub.GetEntry("META-INF/container.xml")!)).Descendants(Container + "rootfile").Single().Attribute("full-path")!;
        var opf = XDocument.Parse(ReadText(epub.GetEntry(rootfile)!));
        var metadata = opf.Root!.Element(Opf + "metadata")!;
        Assert.Equal("Morning Ink 2026-11-21", (string?)metadata.Element(Dc + "title"));
        Assert.Equal("de", (string?)metadata.Element(Dc + "language"));
        Assert.Equal("urn:uuid:0193a1b2-0000-7000-8000-000000000042", (string?)metadata.Element(Dc + "identifier"));

        var folder = Path.GetDirectoryName(rootfile)!;
        var items = opf.Descendants(Opf + "item").ToList();
        Assert.All(items, item => Assert.NotNull(epub.GetEntry(Path.Combine(folder, (string)item.Attribute("href")!).Replace('\\', '/'))));
        Assert.Contains(items, item => (string?)item.Attribute("properties") == "cover-image");
        Assert.Contains(items, item => (string?)item.Attribute("properties") == "nav");
        Assert.Equal(items.Count + 3, epub.Entries.Count); // plus mimetype, container.xml and the package document itself
    }

    [Fact]
    public async Task Every_page_is_well_formed_xhtml_and_the_table_of_contents_lists_every_article()
    {
        using var epub = await RenderAsync();

        var pages = epub.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal)).ToList();
        Assert.All(pages, page => XDocument.Parse(ReadText(page)));
        var nav = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "nav.xhtml")));
        var entries = nav.Descendants(Xhtml + "a").Select(a => a.Value).ToList();
        Assert.Contains("Größere Überraschung bei der Wahl", entries);
        Assert.Contains("<script>alert(1)</script> & Co", entries);
        Assert.Contains("Nur Teaser", entries);
        Assert.Contains("Lokales", entries);
    }

    [Fact]
    public async Task Headlines_without_text_are_briefs_on_the_section_page_not_pages_of_their_own()
    {
        using var epub = await RenderAsync();

        var texts = epub.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal)).Select(e => (e.Name, Text: ReadText(e))).ToList();
        var section = XDocument.Parse(texts.Single(t => t.Text.Contains("Feuer in Schwabing", StringComparison.Ordinal) && !t.Name.StartsWith("nav", StringComparison.Ordinal) && !t.Name.StartsWith("title", StringComparison.Ordinal)).Text);
        Assert.Equal("Lokales", section.Descendants(Xhtml + "h1").Single().Value);
        Assert.Contains(section.Descendants(Xhtml + "a"), a => (string?)a.Attribute("href") == "https://example.com/Feuer%20in%20Schwabing");
        Assert.DoesNotContain(texts, t => t.Text.Contains("<article", StringComparison.Ordinal) && t.Text.Contains("Feuer in Schwabing", StringComparison.Ordinal));
        var nav = XDocument.Parse(texts.Single(t => t.Name == "nav.xhtml").Text);
        Assert.DoesNotContain("Feuer in Schwabing", nav.Descendants(Xhtml + "a").Select(a => a.Value));
    }

    [Fact]
    public async Task Full_articles_are_listed_once_in_the_contents_and_carry_their_section()
    {
        using var epub = await RenderAsync();

        // No page that only repeats the contents: a section without briefs has no page of its own.
        var sectionPages = epub.Entries.Where(e => e.Name.StartsWith("section-", StringComparison.Ordinal)).Select(ReadText).ToList();
        Assert.Single(sectionPages);
        Assert.DoesNotContain("Nur Teaser", sectionPages[0], StringComparison.Ordinal);
        var article = XDocument.Parse(epub.Entries.Select(ReadText).Single(t => t.Contains("<article", StringComparison.Ordinal) && t.Contains("Größere Überraschung", StringComparison.Ordinal)));
        Assert.Equal("Politik", article.Descendants(Xhtml + "p").First(p => (string?)p.Attribute("class") == "kicker").Value);
        Assert.Contains("Auch bei: Süddeutsche Zeitung", article.Root!.Value, StringComparison.Ordinal);
        var summary = article.Descendants(Xhtml + "aside").Single();
        Assert.Contains("Kurz gefasst", summary.Value, StringComparison.Ordinal);
        Assert.Contains("Die Wahl ging anders aus als erwartet. <b>Kein</b> Markup hier.", summary.Value, StringComparison.Ordinal); // text, never markup
        Assert.Empty(summary.Descendants(Xhtml + "b"));
    }

    [Fact]
    public async Task Ncx_gives_entries_with_the_same_target_the_same_play_order()
    {
        // A section's entry leads to its first article, so two entries share a target; epubcheck wants one number for it.
        using var epub = await RenderAsync();

        XNamespace ncx = "http://www.daisy.org/z3986/2005/ncx/";
        var points = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "toc.ncx"))).Descendants(ncx + "navPoint")
            .Select(p => (Src: (string?)p.Element(ncx + "content")?.Attribute("src"), Order: (int)p.Attribute("playOrder")!)) // ! is safe: every navPoint gets one
            .ToList();
        Assert.All(points.GroupBy(p => p.Src), target => Assert.Single(target.Select(p => p.Order).Distinct()));
        Assert.Equal(points.Select(p => p.Src).Distinct().Count(), points.Select(p => p.Order).Distinct().Count());
    }

    [Fact]
    public async Task Article_page_has_text_links_and_local_images_only()
    {
        using var epub = await RenderAsync();

        var page = epub.Entries.Select(ReadText).Single(text => text.Contains("Größere Überraschung", StringComparison.Ordinal) && text.Contains("<article", StringComparison.Ordinal));
        var article = XDocument.Parse(page);
        Assert.Contains("Die Wahlbeteiligung lag bei 78 Prozent", article.Root!.Value, StringComparison.Ordinal);
        Assert.Contains(article.Descendants(Xhtml + "a"), a => (string?)a.Attribute("href") == "https://example.com/wahl");
        Assert.Contains(article.Descendants(Xhtml + "a"), a => (string?)a.Attribute("href") == "https://xn--bcher-kva.de/a%20b");
        var image = Assert.Single(article.Descendants(Xhtml + "img"));
        Assert.Equal($"images/{_imageName}", (string?)image.Attribute("src"));
        Assert.NotNull(epub.GetEntry($"OEBPS/images/{_imageName}"));
        Assert.DoesNotContain(epub.Entries, e => e.FullName.Contains("keys", StringComparison.Ordinal));
    }

    [Fact]
    public async Task After_the_summary_a_link_leads_back_to_the_article_in_the_contents()
    {
        using var epub = await RenderAsync();

        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "article-1-1.xhtml")));
        var back = page.Descendants(Xhtml + "a").Where(a => ((string?)a.Attribute("href"))?.StartsWith("nav.xhtml#", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(2, back.Count); // after the summary, for whom it is enough, and at the end
        var summary = page.Descendants(Xhtml + "aside").Single(e => (string?)e.Attribute("class") == "summary");
        Assert.Contains(back[0], summary.ElementsAfterSelf().First().DescendantsAndSelf()); // right after the summary

        var target = ((string)back[0].Attribute("href")!)["nav.xhtml#".Length..];
        var nav = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "nav.xhtml")));
        var entry = Assert.Single(nav.Descendants(), e => (string?)e.Attribute("id") == target);
        Assert.Equal("article-1-1.xhtml", (string?)entry.Element(Xhtml + "a")?.Attribute("href"));
        Assert.Equal(nav.Descendants().Count(e => e.Attribute("id") is not null), nav.Descendants().Select(e => (string?)e.Attribute("id")).Where(id => id is not null).Distinct().Count()); // ids are unique
    }

    [Fact]
    public async Task An_article_shortened_to_its_summary_keeps_a_page_with_the_summary_and_the_original_link()
    {
        var issue = SampleIssue();
        var shortened = new FetchedArticle(null, "Top 10: Thermostate", new Uri("https://example.com/top10"), null, null, null, null, AiSummary: "Testsieger ist Tado.");
        issue = issue with { Sections = [new IssueSection("Technik", [new IssueArticle(shortened, "heise")])] };

        using var epub = await RenderAsync(issue, "shortened-issue.epub");

        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "article-1-1.xhtml")));
        Assert.Equal("Testsieger ist Tado.", page.Descendants(Xhtml + "aside").Single().Elements(Xhtml + "p").Last().Value);
        Assert.Contains(page.Descendants(Xhtml + "a"), a => (string?)a.Attribute("href") == "https://example.com/top10");
    }

    [Fact]
    public async Task Without_a_summary_the_link_is_only_at_the_end()
    {
        using var epub = await RenderAsync();

        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "article-1-2.xhtml")));
        Assert.Single(page.Descendants(Xhtml + "a"), a => ((string?)a.Attribute("href"))?.StartsWith("nav.xhtml#", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Feedback_links_stand_under_the_article_they_are_for()
    {
        var issue = SampleIssue();
        var politics = issue.Sections[0].Articles[0];
        var links = new FeedbackLinks(new Uri("https://news.example.com/feedback/more"), new Uri("https://news.example.com/feedback/less"));
        issue = issue with { Feedback = new Dictionary<IssueArticle, FeedbackLinks>(ReferenceEqualityComparer.Instance) { [politics] = links } };

        using var epub = await RenderAsync(issue, "feedback.epub");

        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "article-1-1.xhtml")));
        var feedback = page.Descendants(Xhtml + "p").Single(p => (string?)p.Attribute("class") == "feedback");
        Assert.Equal(
            [("https://news.example.com/feedback/more", "Mehr davon"), ("https://news.example.com/feedback/less", "Weniger davon")],
            feedback.Elements(Xhtml + "a").Select(a => ((string?)a.Attribute("href"), a.Value)));
        Assert.DoesNotContain("feedback", ReadText(epub.Entries.Single(e => e.Name == "article-1-2.xhtml")), StringComparison.Ordinal); // only where there are links
    }

    [Fact]
    public async Task Script_in_a_title_stays_text()
    {
        using var epub = await RenderAsync();

        Assert.All(epub.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal)), page =>
            Assert.Empty(XDocument.Parse(ReadText(page)).Descendants(Xhtml + "script")));
    }

    [Fact]
    public async Task Title_page_shows_date_notes_and_headlines()
    {
        using var epub = await RenderAsync();

        var title = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "title.xhtml"))).Root!.Value;
        Assert.Contains("Samstag, 21. November 2026", title, StringComparison.Ordinal);
        Assert.Contains("Ausgabe 42", title, StringComparison.Ordinal);
        Assert.Contains("heise war heute nicht erreichbar", title, StringComparison.Ordinal);
        Assert.Contains("Größere Überraschung bei der Wahl", title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Title_page_shows_the_forecast_for_today_and_the_next_days()
    {
        using var epub = await RenderAsync();

        var title = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "title.xhtml"))).Root!.Value;
        Assert.Contains("Wetter für Berlin", title, StringComparison.Ordinal);
        Assert.Contains("Regenschauer, 1–7 °C, Niederschlag 45 %", title, StringComparison.Ordinal);
        Assert.Contains("Morgens: Bedeckt, 1 °C", title, StringComparison.Ordinal);
        Assert.Contains("Abends: Teilweise bewölkt, 6 °C, 40 %", title, StringComparison.Ordinal);
        Assert.Contains("Sonntag: Regen, 2–5 °C, 65 %", title, StringComparison.Ordinal);
        Assert.Contains("Montag: Schnee, -1–4 °C, 28 %", title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Title_page_has_no_weather_without_a_forecast()
    {
        using var epub = await RenderAsync(SampleIssue() with { Weather = null }, "no-weather.epub");

        Assert.DoesNotContain("Wetter", ReadText(epub.Entries.Single(e => e.Name == "title.xhtml")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rewritten_headline_shows_the_original_below_it_and_the_contents_the_plain_one()
    {
        var issue = SampleIssue();
        var plain = issue.Sections[0].Articles[0] with { Article = issue.Sections[0].Articles[0].Article with { Title = "Wahl: Beteiligung bei 78 Prozent", OriginalTitle = "Du glaubst nicht, wie diese Wahl ausging" } };
        issue = issue with { Sections = [issue.Sections[0] with { Articles = [plain, .. issue.Sections[0].Articles.Skip(1)] }, .. issue.Sections.Skip(1)] };

        using var epub = await RenderAsync(issue, "headline.epub");

        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "article-1-1.xhtml")));
        Assert.Equal("Wahl: Beteiligung bei 78 Prozent", page.Descendants(Xhtml + "h1").Single().Value);
        var original = page.Descendants(Xhtml + "p").Single(p => (string?)p.Attribute("class") == "original-title");
        Assert.Equal("Originaltitel: Du glaubst nicht, wie diese Wahl ausging", original.Value);
        Assert.Contains("Wahl: Beteiligung bei 78 Prozent", NavEntries(epub));
    }

    [Fact]
    public async Task Overview_page_stands_at_its_place_with_its_paragraph_and_links_to_the_articles()
    {
        var issue = SampleIssue();
        var politics = issue.Sections[0].Articles[0];
        issue = issue with { Extras = [new IssueExtra(IssueExtraKind.Overview, "Das Wichtigste in 5 Minuten", 1, new IssueOverview("Die Wahl ging anders aus.", [politics]))] };

        using var epub = await RenderAsync(issue, "overview.epub");

        var spine = Spine(epub);
        var overview = spine.IndexOf("overview.xhtml");
        Assert.True(overview > spine.IndexOf("article-1-1.xhtml") && overview < spine.IndexOf("section-2.xhtml")); // between the two sections
        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "overview.xhtml")));
        Assert.Contains(page.Descendants(Xhtml + "p"), p => p.Value == "Die Wahl ging anders aus.");
        var link = Assert.Single(page.Descendants(Xhtml + "a"));
        Assert.Equal(("article-1-1.xhtml", politics.Article.Title), ((string?)link.Attribute("href"), link.Value));
        Assert.Contains("Das Wichtigste in 5 Minuten", NavEntries(epub));
    }

    [Fact]
    public async Task On_this_day_closes_the_issue_with_years_events_and_the_wikipedia_credit()
    {
        var issue = SampleIssue();
        issue = issue with { Extras = [new IssueExtra(IssueExtraKind.OnThisDay, "An diesem Tag", issue.Sections.Count, Events: [new(1911, "Die erste Bahn fährt nach <Neudorf>.")])] };

        using var epub = await RenderAsync(issue, "on-this-day.epub");

        Assert.Equal("on-this-day.xhtml", Spine(epub)[^1]);
        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "on-this-day.xhtml")));
        Assert.Equal("1911 – Die erste Bahn fährt nach <Neudorf>.", Assert.Single(page.Descendants(Xhtml + "li")).Value); // text, never markup
        Assert.Contains(page.Descendants(Xhtml + "p"), p => p.Value.Contains("CC BY-SA", StringComparison.Ordinal));
        Assert.Contains("An diesem Tag", NavEntries(epub));
    }

    [Fact]
    public async Task Article_of_the_day_follows_the_events_with_its_text_and_a_link()
    {
        var issue = SampleIssue();
        var featured = new FeaturedArticle("Beispielhausen", ["Eine Gemeinde am <Musterbach>.", "Sie hat eine Brücke."], new Uri("https://de.wikipedia.org/wiki/Beispielhausen"));
        issue = issue with { Extras = [new IssueExtra(IssueExtraKind.OnThisDay, "An diesem Tag", issue.Sections.Count, Events: [new(1911, "Die erste Bahn fährt.")], Featured: featured)] };

        using var epub = await RenderAsync(issue, "featured.epub");

        var page = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "on-this-day.xhtml")));
        Assert.Single(page.Descendants(Xhtml + "li"));
        Assert.Equal(("Artikel des Tages", "Beispielhausen"), (page.Descendants(Xhtml + "h2").Single().Value, page.Descendants(Xhtml + "h3").Single().Value));
        Assert.Contains(page.Descendants(Xhtml + "p"), p => p.Value == "Eine Gemeinde am <Musterbach>."); // text, never markup
        Assert.Contains(page.Descendants(Xhtml + "a"), a => (string?)a.Attribute("href") == "https://de.wikipedia.org/wiki/Beispielhausen");
        Assert.Contains(page.Descendants(Xhtml + "p"), p => p.Value.Contains("CC BY-SA", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Weather_placed_further_down_gets_a_page_there_instead_of_the_title_page()
    {
        var issue = SampleIssue() with { Extras = [new IssueExtra(IssueExtraKind.Weather, "Wetter", 2)] };

        using var epub = await RenderAsync(issue, "weather-page.epub");

        Assert.DoesNotContain("Wetter für Berlin", ReadText(epub.Entries.Single(e => e.Name == "title.xhtml")), StringComparison.Ordinal);
        Assert.Contains("Wetter für Berlin", ReadText(epub.Entries.Single(e => e.Name == "weather.xhtml")), StringComparison.Ordinal);
        Assert.Equal("weather.xhtml", Spine(epub)[^1]);
        Assert.Contains("Wetter", NavEntries(epub));
    }

    [Fact]
    public async Task Weather_placed_first_stays_on_the_title_page()
    {
        var issue = SampleIssue() with { Extras = [new IssueExtra(IssueExtraKind.Weather, "Wetter", 0)] };

        using var epub = await RenderAsync(issue, "weather-first.epub");

        Assert.Contains("Wetter für Berlin", ReadText(epub.Entries.Single(e => e.Name == "title.xhtml")), StringComparison.Ordinal);
        Assert.Null(epub.Entries.SingleOrDefault(e => e.Name == "weather.xhtml"));
    }

    [Fact]
    public async Task Cover_is_a_portrait_jpeg()
    {
        using var epub = await RenderAsync();

        using var cover = SKBitmap.Decode(epub.Entries.Single(e => e.Name == "cover.jpg").Open());
        Assert.Equal((1600, 2560), (cover.Width, cover.Height));
    }

    [Fact]
    public async Task Real_pages_render_into_well_formed_pages()
    {
        // The ten saved pages through extraction and sanitizing, as the pipeline would deliver them.
        var sanitizer = new ArticleSanitizer();
        var articles = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData", "Pages"), "*.html").Order().Select(file =>
        {
            var url = new Uri($"https://example.com/{Path.GetFileNameWithoutExtension(file)}");
            var html = ArticleExtractor.Extract(File.ReadAllBytes(file), url) ?? string.Empty;
            return new IssueArticle(new FetchedArticle(null, Path.GetFileNameWithoutExtension(file), url, null, null, null, sanitizer.Html(html, url)), "Test");
        }).ToList();
        var issue = SampleIssue() with { Sections = [new IssueSection("Echte Seiten", articles)] };

        using var epub = await RenderAsync(issue, "real-pages.epub");

        Assert.Equal(10, epub.Entries.Count(e => e.Name.StartsWith("article-", StringComparison.Ordinal)));
        Assert.All(epub.Entries.Where(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal)), page => XDocument.Parse(ReadText(page)));
    }

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    private IssueContent SampleIssue()
    {
        var now = new DateTimeOffset(2026, 11, 21, 6, 0, 0, TimeSpan.FromHours(1));
        FetchedArticle Article(string title, string? content, string? summary = null) =>
            new(null, title, new Uri($"https://example.com/{Uri.EscapeDataString(title)}"), now.AddHours(-2), "Erika Mustermann", summary, content);

        var politics = WithSummary(Article(
            "Größere Überraschung bei der Wahl",
            "<p>Die Wahlbeteiligung lag bei 78 Prozent, mehr dazu <a href=\"https://example.com/wahl\">hier</a> und <a href=\"https://bücher.de/a b\">da</a>.</p>"
            + $"<img src=\"images/{_imageName}\" alt=\"Wahllokal\">"
            + "<img src=\"images/../../keys/key.xml\"><img src=\"images/0000000000000000000000000000000000000000000000000000000000000000.jpg\">"));
        static FetchedArticle WithSummary(FetchedArticle article) => article with { AiSummary = "Die Wahl ging anders aus als erwartet. <b>Kein</b> Markup hier." };
        return new IssueContent(
            Id: Guid.Parse("0193a1b2-0000-7000-8000-000000000042"),
            Title: "Morning Ink",
            Number: 42,
            PublishedAt: now,
            Language: "de",
            Sections:
            [
                new IssueSection("Politik", [new IssueArticle(politics, "tagesschau", [new AlsoReported("Süddeutsche Zeitung", Article("Wahl: größere Überraschung", "<p>Text</p>"))]), new IssueArticle(Article("<script>alert(1)</script> & Co", "<p>Text</p>"), "heise")]),
                new IssueSection("Lokales", [new IssueArticle(Article("Nur Teaser", null, "<p>Kurzfassung</p>"), "Stadt"), new IssueArticle(Article("Feuer in Schwabing", null), "Lokales München")]),
            ],
            UnavailableSources: ["heise"],
            Weather: new WeatherForecast(
                "Berlin",
                [
                    new DayForecast(new DateOnly(2026, 11, 21), 80, 0.5, 6.7, 45),
                    new DayForecast(new DateOnly(2026, 11, 22), 61, 1.9, 5.2, 65),
                    new DayForecast(new DateOnly(2026, 11, 23), 71, -0.7, 3.5, 28),
                ],
                [
                    new DayPartForecast(DayPart.Morning, 3, 1.3, 0),
                    new DayPartForecast(DayPart.Midday, 3, 6.4, 20),
                    new DayPartForecast(DayPart.Evening, 2, 6.1, 40),
                ]));
    }

    private Task<ZipArchive> RenderAsync() => RenderAsync(SampleIssue(), "sample-issue.epub");

    private async Task<ZipArchive> RenderAsync(IssueContent issue, string fileName)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = _dataDirectory })
                .Build())
            .BuildServiceProvider();
        var renderer = services.GetServices<IRenderer>().Single(r => r.FileExtension == ".epub");

        var path = Path.Combine(_dataDirectory, fileName);
        await using (var file = File.Create(path))
        {
            await renderer.RenderAsync(issue, file, CancellationToken.None);
        }

        // CI validates this file with epubcheck.
        if (Environment.GetEnvironmentVariable("MORNINGINK_EPUB_OUTPUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            File.Copy(path, Path.Combine(output, fileName), overwrite: true);
        }

        return ZipFile.OpenRead(path);
    }

    // The reading order, as file names.
    private static List<string> Spine(ZipArchive epub)
    {
        var opf = XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name.EndsWith(".opf", StringComparison.Ordinal))));
        var hrefs = opf.Descendants(Opf + "item").ToDictionary(i => (string)i.Attribute("id")!, i => (string)i.Attribute("href")!);
        return [.. opf.Descendants(Opf + "itemref").Select(r => hrefs[(string)r.Attribute("idref")!])];
    }

    private static List<string> NavEntries(ZipArchive epub) =>
        [.. XDocument.Parse(ReadText(epub.Entries.Single(e => e.Name == "nav.xhtml"))).Descendants(Xhtml + "a").Select(a => a.Value)];

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
