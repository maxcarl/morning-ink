using System.Net;
using Ganss.Xss;

namespace MorningInk.Infrastructure.Processing;

/// <summary>
/// Strict allowlist cleaning for everything that ends up in an issue or the web view. Anything not listed
/// here is removed: scripts, event handlers, styles, forms, frames and every URL scheme except http(s).
/// </summary>
public sealed class ArticleSanitizer
{
    private static readonly string[] ArticleTags =
    [
        "p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6",
        "strong", "b", "em", "i", "u", "s", "small", "sub", "sup", "mark",
        "blockquote", "q", "cite", "code", "pre", "abbr", "time",
        "ul", "ol", "li", "dl", "dt", "dd",
        "a", "img", "figure", "figcaption",
        "table", "caption", "thead", "tbody", "tfoot", "tr", "th", "td",
    ];

    // No "style", "class" or "id": they carry tracking URLs and layout that would break e-ink rendering.
    private static readonly string[] ArticleAttributes = ["href", "src", "alt", "title", "colspan", "rowspan", "datetime", "cite"];

    private static readonly string[] UrlAttributes = ["href", "src", "cite"];

    // Removed together with their content: it is code, styling or form controls, never readable text.
    private static readonly HashSet<string> DroppedWithContent = new(
        ["script", "style", "noscript", "template", "iframe", "object", "embed", "svg", "math", "form", "button", "select", "textarea"],
        StringComparer.OrdinalIgnoreCase);

    private readonly HtmlSanitizer _html = Create(ArticleTags, ArticleAttributes);
    private readonly HtmlSanitizer _text = Create([], []);

    /// <summary>Cleans article HTML; relative links and images become absolute against <paramref name="baseUri"/>.</summary>
    public string Html(string html, Uri baseUri) => _html.Sanitize(html, baseUri.ToString()).Trim();

    /// <summary>Turns a title or name that may contain markup or entities into plain text.</summary>
    public string Text(string html) => WebUtility.HtmlDecode(_text.Sanitize(html)).Trim();

    private static HtmlSanitizer Create(string[] tags, string[] attributes)
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(attributes, StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(["http", "https"], StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(UrlAttributes, StringComparer.OrdinalIgnoreCase),
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<AngleSharp.Css.Dom.CssRuleType>(),
        };

        // Wrappers like <div> or <span> disappear, but their text stays ...
        var sanitizer = new HtmlSanitizer(options) { KeepChildNodes = true };

        // ... except for elements whose content is not text; emptied first, so nothing of them is kept.
        sanitizer.RemovingTag += (_, e) =>
        {
            if (DroppedWithContent.Contains(e.Tag.LocalName))
            {
                e.Tag.InnerHtml = string.Empty;
            }
        };
        return sanitizer;
    }
}
