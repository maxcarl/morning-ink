namespace MorningInk.Domain;

/// <summary>Where articles come from, e.g. an RSS feed.</summary>
public class Source
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required string Name { get; set; }

    public SourceKind Kind { get; set; } = SourceKind.Rss;

    /// <summary>Address of the source, e.g. the feed. Untrusted input: only fetched through the safe HTTP client. Null for local news.</summary>
    public string? Url { get; set; }

    /// <summary>For local news: postal code or town, e.g. "52062" or "Aachen".</summary>
    public string? Place { get; set; }

    /// <summary>For local news: ISO country code of <see cref="Place"/>, e.g. "AT". Null searches worldwide.</summary>
    public string? Country { get; set; }

    /// <summary>Resolved <see cref="Place"/>; resolved again once <see cref="RegionLocation.Query"/> no longer matches.</summary>
    public RegionLocation? Location { get; set; }

    /// <summary>Login used to fetch the source; null for public sources.</summary>
    public Guid? CredentialId { get; set; }

    /// <summary>Whether to fetch each article's web page for the full text; off for feeds that already contain it.</summary>
    public bool FetchFullText { get; set; } = true;

    /// <summary>Most articles taken per issue; the newest win.</summary>
    public int MaxArticles { get; set; } = 20;

    /// <summary>Articles published longer ago are left out. Articles without a date are kept.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(1);

    /// <summary>When not empty, only articles with at least one of these words or phrases in title or teaser.</summary>
    public List<string> IncludeKeywords { get; set; } = [];

    /// <summary>Articles with one of these words or phrases in title or teaser are left out.</summary>
    public List<string> ExcludeKeywords { get; set; } = [];

    /// <summary>Articles with fewer words, after the full text, are left out; null keeps all.</summary>
    public int? MinWords { get; set; }

    /// <summary>Whether to leave out live tickers and live blogs, which are outdated by the time the issue is read.</summary>
    public bool SkipLiveTickers { get; set; }

    /// <summary>
    /// Whether to leave out articles the publisher marks as advertising: "Anzeige:" or "[Sponsored]" in the title, an
    /// advertising category, or a sponsored section in the address. On by default; the word alone in a text is no mark.
    /// </summary>
    public bool SkipAds { get; set; } = true;

    /// <summary>Whether this source's content may only go to an AI on the user's own hardware, never to a cloud provider.</summary>
    public bool LocalAiOnly { get; set; }

    /// <summary>Cache validator from the last fetch, so an unchanged source is not downloaded again.</summary>
    public string? ETag { get; set; }

    /// <summary>Cache validator from the last fetch, used when the server sends no ETag.</summary>
    public DateTimeOffset? LastModified { get; set; }
}
