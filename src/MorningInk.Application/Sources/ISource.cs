using MorningInk.Domain;

namespace MorningInk.Application.Sources;

/// <summary>
/// Fetches articles from one kind of source. A new kind of source is a new implementation plus its
/// registration; existing code stays untouched.
/// </summary>
public interface ISource
{
    /// <summary>The kind of <see cref="Source"/> this implementation handles.</summary>
    SourceKind Kind { get; }

    /// <summary>
    /// Fetches the current articles of <paramref name="source"/>, applying its age and count limits.
    /// Expected problems such as an offline source come back as <see cref="SourceResult.Failure"/>.
    /// </summary>
    Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken);

    /// <summary>
    /// Called once an issue with these articles of <paramref name="source"/> is delivered, e.g. so FreshRSS marks them
    /// read. Most sources need nothing, hence the empty default. Expected problems come back in the result.
    /// </summary>
    /// <param name="articleIds">The articles' own ids, see <see cref="FetchedArticle.Id"/>.</param>
    Task<SourceAcknowledgement> AcknowledgeAsync(Source source, IReadOnlyList<string> articleIds, CancellationToken cancellationToken) =>
        Task.FromResult(SourceAcknowledgement.Nothing);
}

/// <param name="Marked">How many articles the source took note of; 0 when it does nothing with them.</param>
/// <param name="Problem">Why it could not, in English for the log; null when it worked.</param>
public sealed record SourceAcknowledgement(int Marked, string? Problem)
{
    public static readonly SourceAcknowledgement Nothing = new(0, null);
}
