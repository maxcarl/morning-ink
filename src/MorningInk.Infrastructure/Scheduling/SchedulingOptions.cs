namespace MorningInk.Infrastructure.Scheduling;

/// <summary>When issues are caught up and retried. Configured in the "Scheduling" section.</summary>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    /// <summary>A slot missed while Morning Ink was off is caught up at start if it is at most this old.</summary>
    public TimeSpan CatchUpWindow { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Delay before the first retry of a failed delivery; each further retry waits three times as long.</summary>
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Delivery attempts per issue before giving up; the dashboard shows the last error.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;
}
