namespace MorningInk.Application.Wikipedia;

/// <summary>Events that happened on a calendar day in earlier years, as Wikipedia's "On this day" lists them.</summary>
public interface IOnThisDay
{
    /// <summary>A few events of <paramref name="day"/>'s day and month, oldest first; empty when the service is unavailable.</summary>
    /// <param name="country">ISO code of the edition's country; it picks the Wikipedia language. Null takes English.</param>
    Task<IReadOnlyList<Anniversary>> EventsAsync(DateOnly day, string? country, CancellationToken cancellationToken);
}

/// <param name="Year">Year of the event; negative for years before Christ.</param>
/// <param name="Text">One or two sentences about it, as plain text.</param>
public sealed record Anniversary(int Year, string Text);
