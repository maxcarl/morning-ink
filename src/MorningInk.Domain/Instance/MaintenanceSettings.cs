namespace MorningInk.Domain;

/// <summary>When the instance cleans up, set by the admin on the status page. One row at most.</summary>
public class MaintenanceSettings
{
    /// <summary>The time zone the cleanup time is read in until the admin chooses one; the old fixed default.</summary>
    public const string DefaultTimeZoneId = "Etc/UTC";

    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Daily cleanup time, in <see cref="CleanupTimeZoneId"/>.</summary>
    public TimeOnly CleanupTime { get; set; } = new(3, 30);

    /// <summary>IANA time zone of <see cref="CleanupTime"/>, so 3:30 stays 3:30 across daylight saving time.</summary>
    public string CleanupTimeZoneId { get; set; } = DefaultTimeZoneId;
}
