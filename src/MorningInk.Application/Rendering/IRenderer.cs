namespace MorningInk.Application.Rendering;

/// <summary>Turns an issue into a file for a reader, e.g. EPUB. A new format is a new implementation plus its registration.</summary>
public interface IRenderer
{
    /// <summary>File extension including the dot, e.g. ".epub".</summary>
    string FileExtension { get; }

    /// <summary>Media type for delivery, e.g. "application/epub+zip".</summary>
    string MediaType { get; }

    /// <summary>Writes the issue to <paramref name="output"/>.</summary>
    /// <param name="issue">Content with sanitized article HTML, as the pipeline produced it.</param>
    /// <param name="output">Must be seekable, e.g. a file; ZIP-based formats need it.</param>
    /// <param name="cancellationToken">Cancels rendering.</param>
    Task RenderAsync(Issue issue, Stream output, CancellationToken cancellationToken);
}
