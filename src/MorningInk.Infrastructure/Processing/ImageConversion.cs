namespace MorningInk.Infrastructure.Processing;

/// <summary>Outcome of converting one image.</summary>
public abstract record ImageConversion
{
    private ImageConversion()
    {
    }

    /// <param name="Jpeg">Grayscale JPEG, at most <see cref="ImageOptions.MaxEncodedBytes"/>.</param>
    public sealed record Converted(byte[] Jpeg, int Width, int Height) : ImageConversion;

    public sealed record Rejected(ImageRejection Reason) : ImageConversion;
}

/// <summary>Why an image is left out of the issue.</summary>
public enum ImageRejection
{
    /// <summary>Not a format the decoder knows (SVG is deliberately not supported: it can contain scripts).</summary>
    NotAnImage,

    /// <summary>Smaller than <see cref="ImageOptions.MinDimension"/> on one side.</summary>
    Icon,

    /// <summary>More than <see cref="ImageOptions.MaxPixels"/>; never decoded.</summary>
    TooManyPixels,

    /// <summary>Could not be compressed below <see cref="ImageOptions.MaxEncodedBytes"/>.</summary>
    TooLarge,
}
