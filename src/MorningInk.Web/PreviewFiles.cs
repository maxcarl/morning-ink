using System.IO.Compression;

namespace MorningInk.Web;

/// <summary>Serves the pages of a user's preview EPUB, so the preview page can show them in a frame.</summary>
public static class PreviewFiles
{
    private const string ContentFolder = "OEBPS/";

    // Only what the issue's pages need: no scripts, nothing from elsewhere, and framing by this site only.
    private const string ContentSecurityPolicy =
        "default-src 'none'; img-src 'self'; style-src 'self'; script-src 'none'; frame-ancestors 'self'; base-uri 'none'; form-action 'none'";

    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".xhtml"] = "application/xhtml+xml",
        [".css"] = "text/css",
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    public static void MapPreviewFiles(this WebApplication app) =>
        app.MapGet("/preview/file/{*path}", (string path, HttpContext context, PreviewStore previews) =>
        {
            // The fallback policy has already required a signed-in user.
            var userId = context.User.Id();
            if (previews.Get(userId) is not { } file || !MediaTypes.TryGetValue(Path.GetExtension(path), out var mediaType))
            {
                return Results.NotFound();
            }

            // Looked up by exact name inside the ZIP, never as a path on disk: "../" cannot lead anywhere.
            using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
            if (zip.GetEntry(ContentFolder + path) is not { } entry)
            {
                return Results.NotFound();
            }

            using var content = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(content);
            }

            // Replaces the site-wide headers, which forbid any framing.
            context.Response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
            context.Response.Headers.XFrameOptions = "SAMEORIGIN";
            return Results.Bytes(content.ToArray(), mediaType);
        });
}
