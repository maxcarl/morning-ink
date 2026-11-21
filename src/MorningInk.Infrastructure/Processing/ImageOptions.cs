namespace MorningInk.Infrastructure.Processing;

/// <summary>How article images are prepared for e-ink. Configured in the "Images" section.</summary>
public sealed class ImageOptions
{
    public const string SectionName = "Images";

    /// <summary>Images narrower or lower than this are icons, avatars or tracking pixels and are dropped.</summary>
    public int MinDimension { get; set; } = 100;

    /// <summary>Longest side after scaling; enough for current e-readers at full width.</summary>
    public int MaxDimension { get; set; } = 1000;

    /// <summary>Largest image accepted, judged from its header before decoding (protects against pixel bombs).</summary>
    public long MaxPixels { get; set; } = 40_000_000;

    /// <summary>Largest file downloaded per image.</summary>
    public long MaxDownloadBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Largest JPEG kept per image; quality and size are reduced until it fits.</summary>
    public int MaxEncodedBytes { get; set; } = 200 * 1024;

    /// <summary>Images kept per article; the rest are removed.</summary>
    public int MaxPerArticle { get; set; } = 10;

    /// <summary>JPEG quality of the first attempt, 1 to 100.</summary>
    public int Quality { get; set; } = 75;
}
