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

    /// <summary>Cache validator from the last fetch, so an unchanged source is not downloaded again.</summary>
    public string? ETag { get; set; }

    /// <summary>Cache validator from the last fetch, used when the server sends no ETag.</summary>
    public DateTimeOffset? LastModified { get; set; }
}
