namespace MorningInk.Application.Delivery;

/// <summary>Brings a rendered issue to the reader, e.g. by Send-to-Kindle email. A new way is a new implementation plus its registration.</summary>
public interface IDelivery
{
    /// <summary>Delivers the file. Expected problems come back as <see cref="DeliveryResult.Failure"/>, never as exceptions.</summary>
    Task<DeliveryResult> DeliverAsync(DeliveryRequest request, CancellationToken cancellationToken);
}

/// <param name="Recipient">Where the issue goes, e.g. the Send-to-Kindle address. Untrusted input, validated before use.</param>
/// <param name="Title">Issue title; becomes the subject and the attachment's file name.</param>
/// <param name="FilePath">The rendered issue.</param>
/// <param name="FileExtension">Extension of the attachment including the dot, e.g. ".epub".</param>
/// <param name="MediaType">Media type of the attachment, e.g. "application/epub+zip".</param>
public sealed record DeliveryRequest(string Recipient, string Title, string FilePath, string FileExtension, string MediaType);

/// <summary>Outcome of a delivery.</summary>
public abstract record DeliveryResult
{
    private DeliveryResult()
    {
    }

    public sealed record Delivered : DeliveryResult;

    /// <param name="Message">Explanation in English, for logs and as a fallback; never contains credentials.</param>
    /// <param name="Retryable">True for temporary problems (server down, 4xx); false when retrying cannot help (too large, rejected).</param>
    /// <param name="Reason">A stable code such as "Unreachable", which the UI turns into words in the user's language.</param>
    /// <param name="Arguments">The values the words need, e.g. the server's name.</param>
    public sealed record Failure(string Message, bool Retryable, string? Reason = null, IReadOnlyList<string>? Arguments = null) : DeliveryResult
    {
        public IReadOnlyList<string> Arguments { get; init; } = Arguments ?? [];
    }
}
