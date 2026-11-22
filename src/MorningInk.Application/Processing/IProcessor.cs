using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Processing;

/// <summary>
/// One step of the article pipeline, e.g. fetching the full text. Steps run in registration order; a new step
/// is a new implementation plus its registration.
/// </summary>
public interface IProcessor
{
    /// <summary>
    /// Returns the article with this step applied. A step that cannot do its work (page offline, no article
    /// found) returns the article unchanged instead of failing, so the issue still appears.
    /// </summary>
    Task<FetchedArticle> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken);

    /// <summary>Whether the step asks an AI; such steps can be left out where speed matters, e.g. in the preview.</summary>
    bool UsesAi => false;
}
