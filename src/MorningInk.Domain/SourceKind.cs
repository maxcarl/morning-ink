namespace MorningInk.Domain;

/// <summary>How a source is fetched; each kind has its own <c>ISource</c> implementation.</summary>
public enum SourceKind
{
    /// <summary>RSS, Atom or JSON Feed.</summary>
    Rss,

    /// <summary>News about a place, found by a news search; one source per place.</summary>
    Local,
}
