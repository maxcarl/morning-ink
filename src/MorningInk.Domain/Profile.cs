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

    /// <summary>Local time the issue should arrive, in <see cref="TimeZoneId"/>.</summary>
    public TimeOnly DeliveryTime { get; set; } = new(6, 0);

    /// <summary>IANA time zone the delivery time refers to.</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    /// <summary>Sections of the issue, ordered by <see cref="Section.Position"/>.</summary>
    public ICollection<Section> Sections { get; } = [];
}
