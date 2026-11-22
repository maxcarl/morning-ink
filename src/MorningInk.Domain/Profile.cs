namespace MorningInk.Domain;

/// <summary>One newspaper edition: what goes in, when it is built and where it is delivered.</summary>
public class Profile
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    /// <summary>Newspaper title on the cover, e.g. "Morning Ink" or "Die Frühstücksausgabe".</summary>
    public required string Title { get; set; }

    /// <summary>Email address the issue is sent to, e.g. the Send-to-Kindle address.</summary>
    public required string Recipient { get; set; }

    /// <summary>Postal code or town for local news and weather; null leaves both out.</summary>
    public string? Region { get; set; }

    /// <summary>ISO country code, e.g. "DE": postal codes and local news are looked up there. Null searches worldwide.</summary>
    public string? Country { get; set; }

    /// <summary>Resolved <see cref="Region"/>; null until resolved, and stale once <see cref="RegionLocation.Query"/> no longer matches.</summary>
    public RegionLocation? Location { get; set; }

    /// <summary>Local time the issue should arrive, in <see cref="TimeZoneId"/>.</summary>
    public TimeOnly DeliveryTime { get; set; } = new(6, 0);

    /// <summary>IANA time zone the delivery time refers to.</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>How long before <see cref="DeliveryTime"/> the issue is built, so a slow AI is done in time; zero builds at delivery.</summary>
    public TimeSpan LeadTime { get; set; } = TimeSpan.Zero;

    /// <summary>Weekdays with an issue, in <see cref="TimeZoneId"/>; empty means every day.</summary>
    public List<DayOfWeek> Days { get; set; } = [];

    /// <summary>Topics to prefer or leave out when a section's limit cannot take every article.</summary>
    public List<Interest> Interests { get; set; } = [];

    /// <summary>Sections of the issue, ordered by <see cref="Section.Position"/>.</summary>
    public ICollection<Section> Sections { get; } = [];
}
