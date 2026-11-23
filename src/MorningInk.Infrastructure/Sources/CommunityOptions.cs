namespace MorningInk.Infrastructure.Sources;

/// <summary>Where Hacker News and Reddit are asked. Configured in the "Community" section.</summary>
public sealed class CommunityOptions
{
    public const string SectionName = "Community";

    /// <summary>Algolia's Hacker News search: one request brings a day's stories with their points.</summary>
    public string HackerNewsApi { get; set; } = "https://hn.algolia.com/api/v1";

    /// <summary>Reddit, whose subreddits answer with JSON at "/r/{name}/top.json".</summary>
    public string RedditUrl { get; set; } = "https://www.reddit.com";
}
