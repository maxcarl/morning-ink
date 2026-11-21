using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
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

        var politics = Article(
            "Größere Überraschung bei der Wahl",
            "<p>Die Wahlbeteiligung lag bei 78 Prozent, mehr dazu <a href=\"https://example.com/wahl\">hier</a> und <a href=\"https://bücher.de/a b\">da</a>.</p>"
            + $"<img src=\"images/{_imageName}\" alt=\"Wahllokal\">"
            + "<img src=\"images/../../keys/key.xml\"><img src=\"images/0000000000000000000000000000000000000000000000000000000000000000.jpg\">");
        return new IssueContent(
            Id: Guid.Parse("0193a1b2-0000-7000-8000-000000000042"),
            Title: "Morning Ink",
            Number: 42,
            PublishedAt: now,
            Language: "de",
            Sections:
            [
                new IssueSection("Politik", [new IssueArticle(politics, "tagesschau"), new IssueArticle(Article("<script>alert(1)</script> & Co", "<p>Text</p>"), "heise")]),
                new IssueSection("Lokales", [new IssueArticle(Article("Nur Teaser", null, "<p>Kurzfassung</p>"), "Stadt")]),
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

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
