namespace MorningInk.Domain;

/// <summary>How a source is fetched; each kind has its own <c>ISource</c> implementation.</summary>
public enum SourceKind
{
    /// <summary>RSS, Atom or JSON Feed.</summary>
    Rss,

    /// <summary>News about a place, found by a news search; one source per place.</summary>
    Local,

    /// <summary>Unread articles of one category in one's own FreshRSS, through its Google Reader API.</summary>
    FreshRss,

    /// <summary>Newsletters in a folder of the user's mailbox, read over IMAP without changing anything there.</summary>
    Newsletter,

    /// <summary>Hacker News stories from a minimum number of points on.</summary>
    HackerNews,

    /// <summary>The day's top posts of one subreddit, from a minimum score on.</summary>
    Reddit,
}
