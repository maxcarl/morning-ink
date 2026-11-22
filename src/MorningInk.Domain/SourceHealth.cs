namespace MorningInk.Domain;

/// <summary>How a source has been doing in the last issue runs.</summary>
public abstract record SourceHealth
{
    private SourceHealth()
    {
    }

    public sealed record Fine : SourceHealth;

    /// <param name="Since">The first failed run in a row.</param>
    /// <param name="Warning">Failing for days, not just once: worth a note on the overview.</param>
    public sealed record Failing(DateTimeOffset Since, bool Warning) : SourceHealth;

    /// <param name="Since">The first run in a row that brought no article.</param>
    /// <param name="Warning">Empty for a week: the feed may have moved or died.</param>
    public sealed record Empty(DateTimeOffset Since, bool Warning) : SourceHealth;
}
