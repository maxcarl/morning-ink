namespace MorningInk.Infrastructure.Wikipedia;

/// <summary>Where "On this day" comes from. Configured in the "Wikipedia" section.</summary>
public sealed class WikipediaOptions
{
    public const string SectionName = "Wikipedia";

    /// <summary>Wikimedia feed API; the language, "onthisday/selected" and the date are appended.</summary>
    public string FeedUrl { get; set; } = "https://api.wikimedia.org/feed/v1/wikipedia";
}
