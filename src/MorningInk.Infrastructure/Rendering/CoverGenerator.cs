using SkiaSharp;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>
/// Draws the cover the reader shows in its library: title, date and the top headlines, in grayscale.
/// Uses the bundled Literata font (SIL OFL), because the container has no system fonts.
/// </summary>
internal static class CoverGenerator
{
    // 1:1.6 portrait, the ratio Amazon recommends for Kindle covers.
    private const int Width = 1600;
    private const int Height = 2560;
    private const int Margin = 120;
    private const int MaxHeadlines = 6;
    private const int MaxLinesPerHeadline = 3;
    private const int JpegQuality = 85;

    private static readonly Lazy<SKTypeface> Bold = new(() => LoadFont("Literata-Bold.ttf"));
    private static readonly Lazy<SKTypeface> Regular = new(() => LoadFont("Literata-Regular.ttf"));

    public static byte[] Create(string title, string dateLine, IReadOnlyList<string> headlines)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        using (var titleFont = FittingFont(Bold.Value, title, 190, Width - (2 * Margin)))
        using (var dateFont = new SKFont(Regular.Value, 64))
        using (var headlineFont = new SKFont(Bold.Value, 76))
        {
            canvas.Clear(SKColors.White);
            var y = 330f;
            canvas.DrawText(title, Width / 2f, y, SKTextAlign.Center, titleFont, ink);
            y += 70;
            canvas.DrawRect(Margin, y, Width - (2 * Margin), 8, ink);
            y += 110;
            canvas.DrawText(dateLine, Width / 2f, y, SKTextAlign.Center, dateFont, ink);
            y += 60;
            canvas.DrawRect(Margin, y, Width - (2 * Margin), 3, ink);
            y += 180;

            foreach (var headline in headlines.Take(MaxHeadlines))
            {
                foreach (var line in Wrap(headline, headlineFont, Width - (2 * Margin)).Take(MaxLinesPerHeadline))
                {
                    if (y > Height - Margin)
                    {
                        break;
                    }

                    canvas.DrawText(line, Margin, y, SKTextAlign.Left, headlineFont, ink);
                    y += 100;
                }

                y += 80;
            }
        }

        using var gray = bitmap.Copy(SKColorType.Gray8)
            ?? throw new InvalidOperationException("Skia could not convert the cover to grayscale.");
        using var jpeg = gray.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return jpeg.ToArray();
    }

    // Long newspaper titles shrink until they fit on one line.
    private static SKFont FittingFont(SKTypeface typeface, string text, float size, float width)
    {
        var font = new SKFont(typeface, size);
        while (font.Size > 40 && font.MeasureText(text) > width)
        {
            font.Size -= 10;
        }

        return font;
    }

    private static IEnumerable<string> Wrap(string text, SKFont font, float width)
    {
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && font.MeasureText(candidate) > width)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }

    private static SKTypeface LoadFont(string file)
    {
        using var stream = typeof(CoverGenerator).Assembly.GetManifestResourceStream($"Fonts.{file}")
            ?? throw new InvalidOperationException($"Font {file} is not embedded.");
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException($"Font {file} could not be loaded.");
    }
}
