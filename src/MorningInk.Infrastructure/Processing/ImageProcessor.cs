using System.Security.Cryptography;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Processing;

/// <summary>
/// Downloads article images, converts them for e-ink and points the HTML at the local copies
/// (<c>images/&lt;sha256&gt;.jpg</c>). Images that fail or are icons are removed.
/// </summary>
/// <remarks>
/// Runs after sanitizing, because the sanitizer would turn the relative local paths into web URLs. It only
/// removes images or sets <c>src</c> to names it generated itself, so it cannot bring unsanitized markup in.
/// </remarks>
internal sealed partial class ImageProcessor(
    SafeHttpClient http,
    IOptions<ImageOptions> imageOptions,
    IOptions<StorageOptions> storage,
    ILogger<ImageProcessor> logger) : IProcessor
{
    private const string ImageUrlPrefix = "images/";
    private const string ImageExtension = ".jpg";

    private readonly ImageOptions _options = imageOptions.Value;

    public async Task<FetchedArticle> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken) =>
        article with
        {
            Summary = await RewriteAsync(article.Summary, cancellationToken),
            Content = await RewriteAsync(article.Content, cancellationToken),
        };

    private async Task<string?> RewriteAsync(string? html, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("<img", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var document = new HtmlParser().ParseDocument($"<!DOCTYPE html><html><body>{html}</body></html>");
        var kept = 0;
        foreach (var image in document.Images.ToList())
        {
            var name = kept < _options.MaxPerArticle && Uri.TryCreate(image.GetAttribute("src"), UriKind.Absolute, out var url)
                ? await StoreAsync(url, cancellationToken)
                : null;
            if (name is null)
            {
                image.Remove();
                continue;
            }

            image.SetAttribute("src", ImageUrlPrefix + name);
            kept++;
        }

        return document.Body!.InnerHtml; // ! is safe: the document above always has a body
    }

    /// <summary>Returns the file name of the converted image, or null if it is left out.</summary>
    private async Task<string?> StoreAsync(Uri url, CancellationToken cancellationToken)
    {
        var fetched = await http.GetAsync(url, maxResponseBytes: _options.MaxDownloadBytes, cancellationToken: cancellationToken);
        if (fetched is not FetchResult.Success download)
        {
            LogDropped(logger, url.Host, fetched is FetchResult.Failure failure ? failure.Message : "not downloaded");
            return null;
        }

        if (ImageConverter.Convert(download.Content, _options) is not ImageConversion.Converted converted)
        {
            return null;
        }

        // Named by content, never by anything from the page: no path tricks, and identical images are stored once.
        var name = Convert.ToHexStringLower(SHA256.HashData(converted.Jpeg)) + ImageExtension;
        var directory = storage.Value.ImageDirectory;
        var path = Path.Combine(directory, name);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temporary, converted.Jpeg, cancellationToken);

            // Atomic, so a crash never leaves a half-written image under its final name.
            File.Move(temporary, path, overwrite: true);
        }

        return name;
    }

    // Logs only the host: image URLs can carry tracking or session parameters.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped an image from {Host}: {Reason}")]
    private static partial void LogDropped(ILogger logger, string host, string reason);
}
