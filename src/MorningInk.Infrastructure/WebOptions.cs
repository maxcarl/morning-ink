namespace MorningInk.Infrastructure;

/// <summary>How readers reach the instance. Configured in the "Web" section.</summary>
public sealed class WebOptions
{
    public const string SectionName = "Web";

    /// <summary>
    /// The address a reader opens links from the issue with, e.g. "https://news.example.com". Without it, issues have
    /// no feedback links, since a link to an address the reader cannot reach would only fail.
    /// </summary>
    public Uri? PublicUrl { get; set; }
}
