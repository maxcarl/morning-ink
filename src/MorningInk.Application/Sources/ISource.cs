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
}
