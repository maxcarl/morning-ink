namespace MorningInk.Application.Publishing;

/// <summary>Outcome of building a preview of the next issue.</summary>
public abstract record PreviewResult
{
    private PreviewResult()
    {
    }

    /// <param name="File">The issue as it would be sent now, e.g. an EPUB.</param>
    public sealed record Built(byte[] File, string FileExtension, string MediaType, IReadOnlyList<SourceProblem> UnavailableSources) : PreviewResult;

    /// <summary>No new articles right now: the issue would not be sent.</summary>
    public sealed record Empty(IReadOnlyList<SourceProblem> UnavailableSources) : PreviewResult;

    public sealed record ProfileNotFound : PreviewResult;
}
