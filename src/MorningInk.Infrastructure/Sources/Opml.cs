using System.Xml.Linq;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Sources;

/// <summary>What an OPML import did: feeds added, skipped as already there, and skipped as unusable.</summary>
public sealed record OpmlImport(int Added, int Duplicates, int Invalid);

/// <summary>
/// OPML, the format feed readers exchange subscriptions in. Read with the same protection as feeds: no DTDs, nothing
/// loaded from elsewhere. Folders are flattened, since sections are chosen per profile here.
/// </summary>
public static class Opml
{
    /// <summary>Larger files are refused unread; a reader's export with hundreds of feeds stays far below.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>At most this many feeds per import, so one file cannot flood the sources.</summary>
    public const int MaxFeeds = 500;

    /// <summary>Reads at most <see cref="MaxBytes"/>; null when the file is larger or no OPML.</summary>
    public static async Task<IReadOnlyList<(string? Name, string? Url)>?> ReadAsync(Stream file, CancellationToken cancellationToken)
    {
        // Stops right past the limit: a huge upload is refused without reading all of it.
        using var content = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (content.Length <= MaxBytes && (read = await file.ReadAsync(chunk, cancellationToken)) > 0)
        {
            content.Write(chunk, 0, read);
        }

        return content.Length > MaxBytes ? null : Read(content.ToArray());
    }

    /// <summary>The feed outlines of the file, as (name, address as written); null when it is no OPML.</summary>
    /// <remarks>Folders are outlines with children; any other outline is meant as a feed, usable or not.</remarks>
    public static IReadOnlyList<(string? Name, string? Url)>? Read(byte[] content)
    {
        if (FeedParser.LoadXml(content) is not { Root: { Name.LocalName: "opml" } root })
        {
            return null;
        }

        return root.Descendants("outline")
            .Where(o => !o.Elements("outline").Any())
            .Take(MaxFeeds)
            .Select(o => (Name(o), (string?)o.Attribute("xmlUrl")))
            .ToList();
    }

    public static string Write(IEnumerable<Source> feeds)
    {
        var opml = new XElement("opml",
            new XAttribute("version", "2.0"),
            new XElement("head", new XElement("title", "Morning Ink")),
            new XElement("body", feeds.Select(f => new XElement("outline",
                new XAttribute("text", f.Name),
                new XAttribute("title", f.Name),
                new XAttribute("type", "rss"),
                new XAttribute("xmlUrl", f.Url ?? string.Empty)))));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), opml).ToString();
    }

    // Readers write the name as "text", "title" or both.
    private static string? Name(XElement outline) =>
        new[] { (string?)outline.Attribute("title"), (string?)outline.Attribute("text") }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))?.Trim();
}
