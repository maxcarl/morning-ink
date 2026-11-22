using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// Reads RSS 2.0, RSS 1.0 (RDF) and Atom. Built on <see cref="XDocument"/> instead of a feed library, because
/// one unreadable date or an older format should cost a single field, not the whole feed.
/// </summary>
internal static partial class FeedParser
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Rss1 = "http://purl.org/rss/1.0/";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace ContentModule = "http://purl.org/rss/1.0/modules/content/";

    // No DTDs, so neither external entities (XXE) nor entity expansion bombs; no resolver, so nothing is ever loaded.
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    /// <summary>Loads XML with the same protection as feeds (no DTDs, nothing loaded from elsewhere); null when it is not XML.</summary>
    public static XDocument? LoadXml(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, ReaderSettings);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Parses a feed; articles without a usable http(s) link are skipped.</summary>
    /// <param name="content">Raw feed bytes; the XML declaration decides the encoding.</param>
    /// <param name="baseUri">Address the feed came from; relative links resolve against it.</param>
    public static SourceResult Parse(byte[] content, Uri baseUri)
    {
        XElement root;
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, ReaderSettings);
            root = XDocument.Load(reader).Root!; // ! is safe: a document that loads always has a root element
        }
        catch (XmlException e)
        {
            return new SourceResult.Failure($"The feed is not valid XML: {e.Message}");
        }

        IEnumerable<FetchedArticle?>? articles = root.Name switch
        {
            { LocalName: "rss", NamespaceName: "" } => root.Element("channel")?.Elements("item").Select(i => FromRss(i, XNamespace.None, baseUri)) ?? [],
            var name when name == Rdf + "RDF" => root.Elements(Rss1 + "item").Select(i => FromRss(i, Rss1, baseUri)),
            var name when name == Atom + "feed" => root.Elements(Atom + "entry").Select(e => FromAtom(e, baseUri)),
            _ => null,
        };

        return articles is null
            ? new SourceResult.Failure("This is not an RSS, RDF or Atom feed.")
            : new SourceResult.Success(articles.OfType<FetchedArticle>().ToList(), ETag: null, LastModified: null);
    }

    /// <summary>The feed's own title (RSS channel, RDF channel or Atom feed), or null if it has none or is no feed.</summary>
    public static string? ReadTitle(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, ReaderSettings);
            var root = XDocument.Load(reader).Root;
            var title = root?.Element("channel")?.Element("title") ?? root?.Element(Rss1 + "channel")?.Element(Rss1 + "title") ?? root?.Element(Atom + "title");
            return Text(title);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>Reads RFC 822 dates (RSS) and ISO 8601 dates (Atom, Dublin Core); null if unreadable.</summary>
    public static DateTimeOffset? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // .NET expects "+01:00" where RFC 822 writes "+0100".
        var normalized = NumericOffset().Replace(text.Trim(), "$1:$2");
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }

    // RSS 2.0 items have no namespace, RSS 1.0 items live in the RSS 1.0 namespace; the structure is the same.
    private static FetchedArticle? FromRss(XElement item, XNamespace ns, Uri baseUri)
    {
        var guid = item.Element(ns + "guid");
        var guidIsLink = guid is not null && !string.Equals((string?)guid.Attribute("isPermaLink"), "false", StringComparison.OrdinalIgnoreCase);
        var url = ResolveLink(Text(item, ns + "link") ?? (guidIsLink ? Text(guid) : null), baseUri);
        if (url is null)
        {
            return null;
        }

        return new FetchedArticle(
            Id: Text(guid) ?? (string?)item.Attribute(Rdf + "about"),
            Title: Text(item, ns + "title") ?? url.ToString(),
            Url: url,
            PublishedAt: ParseDate(Text(item, ns + "pubDate") ?? Text(item, DublinCore + "date")),
            Author: Text(item, DublinCore + "creator") ?? Text(item, ns + "author"),
            Summary: Text(item, ns + "description"),
            Content: Text(item, ContentModule + "encoded"));
    }

    private static FetchedArticle? FromAtom(XElement entry, Uri baseUri)
    {
        // The article itself is the "alternate" link; a link without rel means the same.
        var link = entry.Elements(Atom + "link")
            .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate");
        var url = ResolveLink((string?)link?.Attribute("href"), baseUri);
        if (url is null)
        {
            return null;
        }

        return new FetchedArticle(
            Id: Text(entry, Atom + "id"),
            Title: Text(entry, Atom + "title") ?? url.ToString(),
            Url: url,
            PublishedAt: ParseDate(Text(entry, Atom + "published") ?? Text(entry, Atom + "updated")),
            Author: Text(entry.Element(Atom + "author"), Atom + "name"),
            Summary: Text(entry, Atom + "summary"),
            Content: Text(entry, Atom + "content"));
    }

    // Feeds are untrusted: links like "javascript:" or "file:" must not reach the newspaper.
    private static Uri? ResolveLink(string? link, Uri baseUri) =>
        link is not null && Uri.TryCreate(baseUri, link.Trim(), out var url) && UrlPolicy.Check(url) is null ? url : null;

    private static string? Text(XElement? parent, XName name) => Text(parent?.Element(name));

    private static string? Text(XElement? element) =>
        string.IsNullOrWhiteSpace(element?.Value) ? null : element.Value.Trim();

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex NumericOffset();
}
