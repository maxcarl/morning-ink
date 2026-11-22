using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using MorningInk.Application.Rendering;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>
/// Writes an issue as EPUB 3 with an NCX fallback: title page, table of contents, one page per section and
/// per article, a generated cover and the converted images.
/// </summary>
internal sealed class EpubRenderer(IOptions<StorageOptions> storage) : IRenderer
{
    private const string XhtmlType = "application/xhtml+xml";
    private const string JpegType = "image/jpeg";
    private const string CoverId = "cover-image";
    private const string TitleHref = "title.xhtml";
    private const string NavHref = "nav.xhtml";

    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Async = true,
    };

    public string FileExtension => ".epub";

    public string MediaType => "application/epub+zip";

    public async Task RenderAsync(IssueContent issue, Stream output, CancellationToken cancellationToken)
    {
        // A ZIP written to a non-seekable stream gets data descriptors, which EPUB readers reject for "mimetype".
        if (!output.CanSeek)
        {
            throw new ArgumentException("EPUB output must be seekable, e.g. a file.", nameof(output));
        }

        var texts = EpubTexts.For(issue.Language);
        var imageDirectory = storage.Value.ImageDirectory;
        var bookTitle = issue.BookTitle;

        // By reference: two equal articles in different sections are still two pages.
        var bodies = issue.Sections.SelectMany(s => s.Articles).ToDictionary<IssueArticle, IssueArticle, ArticleBody.Result>(
            a => a,
            a => ArticleBody.ToXhtml(a.Article.Content ?? a.Article.Summary ?? string.Empty, name => File.Exists(Path.Combine(imageDirectory, name))),
            ReferenceEqualityComparer.Instance);

        // Headline-only news go on their section's briefs page; everything else gets a page and a contents entry.
        var full = issue.Sections.Select(s => (IReadOnlyList<IssueArticle>)s.Articles.Where(a => !EpubPages.IsBrief(bodies[a])).ToList()).ToList();
        var briefs = issue.Sections.Select(s => (IReadOnlyList<IssueArticle>)s.Articles.Where(a => EpubPages.IsBrief(bodies[a])).ToList()).ToList();

        // File names by position, never from titles.
        var hrefs = new Dictionary<IssueArticle, string>(ReferenceEqualityComparer.Instance);
        for (var s = 0; s < issue.Sections.Count; s++)
        {
            for (var a = 0; a < full[s].Count; a++)
            {
                hrefs[full[s][a]] = $"article-{s + 1}-{a + 1}.xhtml";
            }

            foreach (var brief in briefs[s])
            {
                hrefs[brief] = BriefsHrefOf(s);
            }
        }

        string HrefOf(IssueArticle article) => hrefs[article];
        static string BriefsHrefOf(int index) => $"section-{index + 1}.xhtml";
        string SectionHrefOf(int index) => full[index].Count > 0 ? HrefOf(full[index][0]) : BriefsHrefOf(index);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var stamp = issue.PublishedAt;

        // The mimetype entry must come first and stay uncompressed, so readers can identify the file.
        await AddAsync(zip, "mimetype", Encoding.ASCII.GetBytes(MediaType), stamp, CompressionLevel.NoCompression, cancellationToken);
        await AddAsync(zip, "META-INF/container.xml", EpubPackage.ContainerXml(), stamp, cancellationToken);

        var items = new List<EpubPackage.Item>
        {
            new(CoverId, "cover.jpg", JpegType, Properties: "cover-image"),
            new("css", EpubPages.StyleSheet, "text/css"),
            new("ncx", EpubPackage.NcxFile, "application/x-dtbncx+xml"),
            new("title", TitleHref, XhtmlType, InSpine: true),
            new("nav", NavHref, XhtmlType, Properties: "nav", InSpine: true),
        };

        await AddContentAsync(zip, "cover.jpg", CoverGenerator.Create(EpubPages.Cover(issue, texts)), stamp, cancellationToken);
        await AddContentAsync(zip, EpubPages.StyleSheet, Encoding.UTF8.GetBytes(EpubPages.Css), stamp, cancellationToken);
        await AddContentAsync(zip, TitleHref, EpubPages.TitlePage(issue, texts, HrefOf), stamp, cancellationToken);
        await AddContentAsync(zip, NavHref, EpubPages.Navigation(issue, texts, TitleHref, SectionHrefOf, s => full[s], HrefOf), stamp, cancellationToken);

        var images = new HashSet<string>(StringComparer.Ordinal);
        for (var s = 0; s < issue.Sections.Count; s++)
        {
            var section = issue.Sections[s];
            foreach (var article in full[s])
            {
                var body = bodies[article];
                items.Add(new(Path.GetFileNameWithoutExtension(HrefOf(article)), HrefOf(article), XhtmlType, InSpine: true));
                await AddContentAsync(zip, HrefOf(article), EpubPages.ArticlePage(article, section.Name, body.Nodes, issue.PublishedAt, texts), stamp, cancellationToken);

                foreach (var image in body.Images.Where(images.Add))
                {
                    items.Add(new($"img-{Path.GetFileNameWithoutExtension(image)}", $"images/{image}", JpegType));
                    await AddContentAsync(zip, $"images/{image}", await File.ReadAllBytesAsync(Path.Combine(imageDirectory, image), cancellationToken), stamp, cancellationToken);
                }
            }

            // "In brief" at the end of the section, as in a printed paper.
            if (briefs[s].Count > 0)
            {
                items.Add(new($"s{s + 1}", BriefsHrefOf(s), XhtmlType, InSpine: true));
                await AddContentAsync(zip, BriefsHrefOf(s), EpubPages.BriefsPage(section, briefs[s], issue.PublishedAt, texts), stamp, cancellationToken);
            }
        }

        var toc = new List<EpubPackage.TocEntry> { new(texts.TitlePage, TitleHref, []) };
        toc.AddRange(issue.Sections.Select((section, index) => new EpubPackage.TocEntry(
            section.Name,
            SectionHrefOf(index),
            full[index].Select(a => new EpubPackage.TocEntry(a.Article.Title, HrefOf(a), [])).ToList())));
        await AddContentAsync(zip, EpubPackage.NcxFile, EpubPackage.NcxDocument(issue, bookTitle, toc), stamp, cancellationToken);
        await AddContentAsync(zip, EpubPackage.PackageFile, EpubPackage.PackageDocument(issue, bookTitle, CoverId, items), stamp, cancellationToken);
    }

    private static Task AddContentAsync(ZipArchive zip, string href, XDocument document, DateTimeOffset stamp, CancellationToken cancellationToken) =>
        AddAsync(zip, $"{EpubPackage.ContentFolder}/{href}", document, stamp, cancellationToken);

    private static Task AddContentAsync(ZipArchive zip, string href, byte[] content, DateTimeOffset stamp, CancellationToken cancellationToken) =>
        AddAsync(zip, $"{EpubPackage.ContentFolder}/{href}", content, stamp, CompressionLevel.Optimal, cancellationToken);

    private static async Task AddAsync(ZipArchive zip, string path, XDocument document, DateTimeOffset stamp, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = stamp;
        await using var stream = await entry.OpenAsync(cancellationToken);
        await using var writer = XmlWriter.Create(stream, WriterSettings);
        await document.SaveAsync(writer, cancellationToken);
    }

    private static async Task AddAsync(ZipArchive zip, string path, byte[] content, DateTimeOffset stamp, CompressionLevel level, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, level);
        entry.LastWriteTime = stamp;
        await using var stream = await entry.OpenAsync(cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
    }
}
