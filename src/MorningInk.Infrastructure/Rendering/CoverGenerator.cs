using SkiaSharp;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>A story on the cover: its section as a kicker, and its headline.</summary>
internal sealed record CoverStory(string? Section, string Headline);

/// <summary>What the cover shows.</summary>
/// <param name="Lead">The top story, set large.</param>
/// <param name="More">Further stories for the two columns below, as many as fit.</param>
/// <param name="Weather">Today's weather in one line; null leaves the line out.</param>
/// <param name="Footer">A small last line, e.g. how many articles the issue has.</param>
internal sealed record CoverPage(string Title, string Date, string IssueLabel, CoverStory Lead, IReadOnlyList<CoverStory> More, string? Weather, string? Footer);

/// <summary>
/// Draws the cover the reader shows in its library, laid out like a front page: ears with date and number, the
/// nameplate over a double rule, the lead story large, more stories in two columns, the weather at the foot.
/// Grayscale for e-ink; the bundled Literata font (SIL OFL), because the container has no system fonts.
/// </summary>
/// <remarks>The library shows it small, so the nameplate and the lead headline carry it; the rest is for the full view.</remarks>
internal static class CoverGenerator
{
    // 1:1.6 portrait, the ratio Amazon recommends for Kindle covers.
    private const int Width = 1600;
    private const int Height = 2560;
    private const int Margin = 110;
    private const int ColumnGap = 90;
    private const int JpegQuality = 85;

    private const float NameplateSize = 250;
    private const int LeadLines = 4;
    private const int StoryLines = 4;

    private static readonly SKColor Ink = SKColors.Black;
    private static readonly SKColor Gray = new(0x55, 0x55, 0x55);

    private static readonly Lazy<SKTypeface> Bold = new(() => LoadFont("Literata-Bold.ttf"));
    private static readonly Lazy<SKTypeface> Regular = new(() => LoadFont("Literata-Regular.ttf"));

    public static byte[] Create(CoverPage page)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            Draw(canvas, page);
        }

        using var gray = bitmap.Copy(SKColorType.Gray8)
            ?? throw new InvalidOperationException("Skia could not convert the cover to grayscale.");
        using var jpeg = gray.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return jpeg.ToArray();
    }

    private static void Draw(SKCanvas canvas, CoverPage page)
    {
        using var ink = new SKPaint { Color = Ink, IsAntialias = true };
        using var gray = new SKPaint { Color = Gray, IsAntialias = true };
        using var ears = new SKFont(Regular.Value, 46);
        using var kicker = new SKFont(Regular.Value, 40);
        using var leadFont = new SKFont(Bold.Value, 112);
        using var storyFont = new SKFont(Bold.Value, 54);
        using var weatherFont = new SKFont(Regular.Value, 52);
        using var footerFont = new SKFont(Regular.Value, 40);
        const float content = Width - (2 * Margin);

        // Ears: date left, issue number right, over a thin rule.
        var y = 150f;
        canvas.DrawText(page.Date, Margin, y, SKTextAlign.Left, ears, gray);
        canvas.DrawText(page.IssueLabel, Width - Margin, y, SKTextAlign.Right, ears, gray);
        y += 40;
        canvas.DrawRect(Margin, y, content, 3, ink);

        // Nameplate over the double rule, as in the web UI's masthead.
        using (var nameplate = FittingFont(Bold.Value, page.Title, NameplateSize, content))
        {
            y += 40 + (nameplate.Size * 0.82f);
            canvas.DrawText(page.Title, Width / 2f, y, SKTextAlign.Center, nameplate, ink);
        }

        y += 50;
        canvas.DrawRect(Margin, y, content, 12, ink);
        canvas.DrawRect(Margin, y + 22, content, 4, ink);

        // The lead story.
        y += 140;
        if (page.Lead.Section is { } leadSection)
        {
            canvas.DrawText(leadSection.ToUpperInvariant(), Margin, y, SKTextAlign.Left, kicker, gray);
            y += 125;
        }

        foreach (var line in Wrap(page.Lead.Headline, leadFont, content, LeadLines))
        {
            canvas.DrawText(line, Margin, y, SKTextAlign.Left, leadFont, ink);
            y += 128;
        }

        y += 10;
        canvas.DrawRect(Margin, y, content, 3, ink);

        // More stories in two columns, each filled from the top until the foot.
        var bottom = Height - (page.Weather is null && page.Footer is null ? Margin : 330f);
        var columnWidth = (content - ColumnGap) / 2;
        var top = y + 110;
        var columnTops = new[] { top, top };
        foreach (var story in page.More)
        {
            var lines = Wrap(story.Headline, storyFont, columnWidth, StoryLines);
            var height = (story.Section is null ? 0 : 62) + (lines.Count * 68) + 64;

            // Into the emptier column; a story that fits in neither is skipped, so a shorter one can still fill the gap.
            var column = columnTops[0] <= columnTops[1] ? 0 : 1;
            if (columnTops[column] + height > bottom)
            {
                column = 1 - column;
                if (columnTops[column] + height > bottom)
                {
                    continue;
                }
            }

            var x = Margin + (column * (columnWidth + ColumnGap));
            var cy = columnTops[column];
            if (story.Section is { } section)
            {
                canvas.DrawText(section.ToUpperInvariant(), x, cy, SKTextAlign.Left, kicker, gray);
                cy += 62;
            }

            foreach (var line in lines)
            {
                canvas.DrawText(line, x, cy, SKTextAlign.Left, storyFont, ink);
                cy += 68;
            }

            columnTops[column] = cy + 64;
        }

        if (columnTops[1] > top)
        {
            canvas.DrawRect(Margin + columnWidth + (ColumnGap / 2f) - 1, top - 60, 2, Math.Max(columnTops[0], columnTops[1]) - top - 20, ink);
        }

        // The foot: today's weather and how much there is to read.
        if (page.Weather is not null || page.Footer is not null)
        {
            canvas.DrawRect(Margin, Height - 290, content, 12, ink);
            canvas.DrawRect(Margin, Height - 268, content, 4, ink);
            if (page.Weather is { } weather)
            {
                using var fitting = FittingFont(Regular.Value, weather, weatherFont.Size, content);
                canvas.DrawText(weather, Width / 2f, Height - 170, SKTextAlign.Center, fitting, ink);
            }

            if (page.Footer is { } footer)
            {
                canvas.DrawText(footer, Width / 2f, Height - 95, SKTextAlign.Center, footerFont, gray);
            }
        }
    }

    // Long newspaper titles and weather lines shrink until they fit on one line.
    private static SKFont FittingFont(SKTypeface typeface, string text, float size, float width)
    {
        var font = new SKFont(typeface, size);
        while (font.Size > 30 && font.MeasureText(text) > width)
        {
            font.Size -= 6;
        }

        return font;
    }

    // At most maxLines lines; a headline that needs more ends in an ellipsis.
    private static List<string> Wrap(string text, SKFont font, float width, int maxLines)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in Words(text, font, width))
        {
            var candidate = line.Length == 0 ? word : line.EndsWith('-') ? line + word : $"{line} {word}";
            if (line.Length > 0 && font.MeasureText(candidate) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        if (lines.Count <= maxLines)
        {
            return lines;
        }

        var cut = lines.Take(maxLines).ToList();
        var last = cut[^1];
        while (last.Length > 0 && font.MeasureText(last + " …") > width)
        {
            var space = last.LastIndexOf(' ');
            last = space > 0 ? last[..space] : string.Empty;
        }

        cut[^1] = last.Length == 0 ? "…" : last + " …";
        return cut;
    }

    // German compounds can be wider than a column: such a word is split after its hyphens, which keep their place
    // at the end of a line. Joined back without a space when they land on the same line.
    private static IEnumerable<string> Words(string text, SKFont font, float width)
    {
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (font.MeasureText(word) <= width || !word.Contains('-', StringComparison.Ordinal))
            {
                yield return word;
                continue;
            }

            var parts = word.Split('-');
            for (var i = 0; i < parts.Length; i++)
            {
                yield return i < parts.Length - 1 ? parts[i] + "-" : parts[i];
            }
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
