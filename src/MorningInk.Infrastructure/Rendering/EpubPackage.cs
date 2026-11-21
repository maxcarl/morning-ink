using System.Xml.Linq;
using MorningInk.Application.Rendering;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>The EPUB bookkeeping files: container.xml, the package document (OPF) and the legacy NCX table of contents.</summary>
internal static class EpubPackage
{
    public const string ContentFolder = "OEBPS";
    public const string PackageFile = "content.opf";
    public const string NcxFile = "toc.ncx";

    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Ncx = "http://www.daisy.org/z3986/2005/ncx/";

    /// <summary>A file in the book. <paramref name="Href"/> is relative to the content folder.</summary>
    public sealed record Item(string Id, string Href, string MediaType, string? Properties = null, bool InSpine = false);

    /// <summary>A table-of-contents entry; children make it a group, e.g. a section with its articles.</summary>
    public sealed record TocEntry(string Label, string Href, IReadOnlyList<TocEntry> Children);

    public static XDocument ContainerXml() =>
        new(new XElement(Container + "container", new XAttribute("version", "1.0"),
            new XElement(Container + "rootfiles",
                new XElement(Container + "rootfile",
                    new XAttribute("full-path", $"{ContentFolder}/{PackageFile}"),
                    new XAttribute("media-type", "application/oebps-package+xml")))));

    public static XDocument PackageDocument(Issue issue, string bookTitle, string coverId, IReadOnlyList<Item> items) =>
        new(new XElement(Opf + "package",
            new XAttribute("version", "3.0"),
            new XAttribute("unique-identifier", "uid"),
            new XAttribute(XNamespace.Xml + "lang", issue.Language),
            new XElement(Opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", Dc),
                new XElement(Dc + "identifier", new XAttribute("id", "uid"), Identifier(issue)),
                new XElement(Dc + "title", bookTitle),
                new XElement(Dc + "language", issue.Language),
                new XElement(Dc + "creator", issue.Title),
                new XElement(Dc + "date", EpubPages.Iso(issue.PublishedAt)),
                new XElement(Opf + "meta", new XAttribute("property", "dcterms:modified"), EpubPages.Iso(issue.PublishedAt)),

                // EPUB 2 style cover hint; Kindle conversion still looks for it.
                new XElement(Opf + "meta", new XAttribute("name", "cover"), new XAttribute("content", coverId))),
            new XElement(Opf + "manifest", items.Select(item => new XElement(Opf + "item",
                new XAttribute("id", item.Id),
                new XAttribute("href", item.Href),
                new XAttribute("media-type", item.MediaType),
                item.Properties is null ? null : new XAttribute("properties", item.Properties)))),
            new XElement(Opf + "spine", new XAttribute("toc", "ncx"),
                items.Where(i => i.InSpine).Select(i => new XElement(Opf + "itemref", new XAttribute("idref", i.Id))))));

    /// <summary>NCX for readers and converters that predate EPUB 3 navigation, Kindle among them.</summary>
    public static XDocument NcxDocument(Issue issue, string bookTitle, IReadOnlyList<TocEntry> toc)
    {
        var order = 0;
        XElement Point(TocEntry entry)
        {
            order++;
            return new XElement(Ncx + "navPoint",
                new XAttribute("id", $"np{order}"),
                new XAttribute("playOrder", order),
                new XElement(Ncx + "navLabel", new XElement(Ncx + "text", entry.Label)),
                new XElement(Ncx + "content", new XAttribute("src", entry.Href)),
                entry.Children.Select(Point).ToList());
        }

        return new(new XElement(Ncx + "ncx",
            new XAttribute("version", "2005-1"),
            new XAttribute(XNamespace.Xml + "lang", issue.Language),
            new XElement(Ncx + "head",
                new XElement(Ncx + "meta", new XAttribute("name", "dtb:uid"), new XAttribute("content", Identifier(issue))),
                new XElement(Ncx + "meta", new XAttribute("name", "dtb:depth"), new XAttribute("content", "2")),
                new XElement(Ncx + "meta", new XAttribute("name", "dtb:totalPageCount"), new XAttribute("content", "0")),
                new XElement(Ncx + "meta", new XAttribute("name", "dtb:maxPageNumber"), new XAttribute("content", "0"))),
            new XElement(Ncx + "docTitle", new XElement(Ncx + "text", bookTitle)),
            new XElement(Ncx + "navMap", toc.Select(Point).ToList())));
    }

    private static string Identifier(Issue issue) => $"urn:uuid:{issue.Id}";
}
