using System.IO.Compression;
using SkiaSharp;

namespace MorningInk.Infrastructure.Tests.Rendering;

/// <summary>Minimal EPUB files for tests that only need what lies inside, not a whole issue.</summary>
public static class TestEpub
{
    private const int CoverHeight = 2560;
    private const int JpegQuality = 80;

    /// <summary>Writes an EPUB with only a cover, 1600 × 2560 like the generated ones, or narrower to tell a second one apart.</summary>
    public static async Task WriteWithCoverAsync(string path, int width = 1600)
    {
        using var bitmap = new SKBitmap(width, CoverHeight);
        bitmap.Erase(SKColors.Gray);
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        await using var file = File.Create(path);
        await using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        await using var entry = await zip.CreateEntry("OEBPS/cover.jpg").OpenAsync();
        await entry.WriteAsync(jpeg.ToArray());
    }
}
