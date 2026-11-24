namespace MorningInk.Application.Wikipedia;

/// <summary>Wikipedia's article of the day, as its editors pick it for the main page.</summary>
public interface IFeaturedArticle
{
    /// <summary>The article of <paramref name="day"/>; null when there is none or the service is unavailable.</summary>
    /// <param name="country">ISO code of the edition's country; it picks the Wikipedia language. Null takes English.</param>
    Task<FeaturedArticle?> ArticleAsync(DateOnly day, string? country, CancellationToken cancellationToken);
}

/// <param name="Title">The article's title, as plain text.</param>
/// <param name="Paragraphs">Its introduction as plain-text paragraphs.</param>
/// <param name="Url">The article on Wikipedia, always https.</param>
public sealed record FeaturedArticle(string Title, IReadOnlyList<string> Paragraphs, Uri Url);
