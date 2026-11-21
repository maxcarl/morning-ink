namespace MorningInk.Domain;

/// <summary>One newspaper edition: what goes in, when it is built and where it is delivered.</summary>
public class Profile
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required string Title { get; set; }

    public required string Recipient { get; set; }

    /// <summary>Postal code or town for local news and weather; null leaves both out.</summary>
    public string? Region { get; set; }

    public TimeOnly DeliveryTime { get; set; } = new(6, 0);

    /// <summary>IANA time zone the delivery time refers to.</summary>
    public string TimeZoneId { get; set; } = "Europe/Berlin";

    public ICollection<Section> Sections { get; } = [];
}
