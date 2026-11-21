namespace MorningInk.Application.Publishing;

/// <summary>Outcome of a publishing run.</summary>
public abstract record PublishResult
{
    private PublishResult()
    {
    }

    /// <param name="UnavailableSources">Sources that failed; the issue mentions them.</param>
    public sealed record Delivered(Guid IssueId, int Articles, IReadOnlyList<SourceProblem> UnavailableSources) : PublishResult;

    /// <summary>The issue for this slot was delivered before; nothing was sent.</summary>
    public sealed record AlreadyDelivered(Guid IssueId) : PublishResult;

    /// <summary>Another run for the same profile is in progress.</summary>
    public sealed record AlreadyRunning : PublishResult;

    public sealed record ProfileNotFound : PublishResult;

    /// <summary>No new articles; nothing was sent.</summary>
    public sealed record NoNewArticles(Guid IssueId, IReadOnlyList<SourceProblem> UnavailableSources) : PublishResult;

    /// <param name="Retryable">Whether running again later can help; the next run sends the same file.</param>
    /// <param name="Attempts">Delivery attempts so far, including this one; the scheduler backs off with it.</param>
    public sealed record DeliveryFailed(Guid IssueId, string Message, bool Retryable, int Attempts) : PublishResult;
}

/// <summary>A source that could not be used today, and why.</summary>
public sealed record SourceProblem(string SourceName, string Message);
