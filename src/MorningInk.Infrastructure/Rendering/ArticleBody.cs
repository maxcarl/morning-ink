using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>Turns sanitized article HTML into XHTML nodes for an EPUB page.</summary>
internal static partial class ArticleBody
{
    /// <summary>XHTML nodes plus the local images they reference, by file name.</summary>
    public sealed record Result(IReadOnlyList<XNode> Nodes, IReadOnlyList<string> Images);

    /// <param name="html">Sanitized HTML; images already point at <c>images/&lt;sha256&gt;.jpg</c>.</param>
    /// <param name="imageExists">Whether a converted image with this file name is stored.</param>
    public static Result ToXhtml(string html, Func<string, bool> imageExists)
    {
        var document = new HtmlParser().ParseDocument($"<!DOCTYPE html><html><body>{html}</body></html>");
        var images = new List<string>();
        foreach (var image in document.Images.ToList())
        {
            // Defense in depth: only names the image step generates are read, so nothing like
            // "images/../../keys/key.xml" can pull other files from the data directory into the book.
            var match = LocalImage().Match(image.GetAttribute("src") ?? string.Empty);
            if (!match.Success || !imageExists(match.Groups[1].Value))
            {
                image.Remove();
                continue;
            }

            images.Add(match.Groups[1].Value);

            // EPUB requires alt; an empty one marks the image as decorative.
            if (!image.HasAttribute("alt"))
            {
                image.SetAttribute("alt", string.Empty);
            }
        }

        // Readers reject unescaped URLs (spaces, umlauts in the host); links that are no valid URL lose their target.
        foreach (var link in document.QuerySelectorAll("a[href]"))
        {
            if (Uri.TryCreate(link.GetAttribute("href"), UriKind.Absolute, out var url))
            {
                link.SetAttribute("href", ToEpubUrl(url));
            }
            else
            {
                link.RemoveAttribute("href");
            }
        }

        var formatter = new XhtmlMarkupFormatter();
        var xhtml = string.Concat(document.Body!.ChildNodes.Select(node => node.ToHtml(formatter))); // ! is safe: the document above always has a body

        // Parsing again proves the result is well-formed XML; a broken page would make the reader reject the book.
        var wrapper = XElement.Parse($"<div xmlns=\"{EpubPages.Xhtml.NamespaceName}\">{xhtml}</div>");
        return new Result(wrapper.Nodes().ToList(), images);
    }

    /// <summary>Fully escaped form with a punycode host, as EPUB requires.</summary>
    public static string ToEpubUrl(Uri url) => new UriBuilder(url) { Host = url.IdnHost }.Uri.AbsoluteUri;

    [GeneratedRegex("^images/([0-9a-f]{64}\\.jpg)$")]
    private static partial Regex LocalImage();
}
