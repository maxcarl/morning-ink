using SkiaSharp;

namespace MorningInk.Infrastructure.Processing;

/// <summary>Turns an untrusted image into a small grayscale JPEG for e-ink, or rejects it.</summary>
internal static class ImageConverter
{
    private static readonly int[] FallbackQualities = [60, 45];
    private const float ShrinkFactor = 0.75f;

    public static ImageConversion Convert(byte[] data, ImageOptions options)
    {
        using var encoded = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(encoded);
        if (codec is null)
        {
            return new ImageConversion.Rejected(ImageRejection.NotAnImage);
        }

        // The codec only read the header so far: check the size before a single pixel is decoded.
        var (width, height) = (codec.Info.Width, codec.Info.Height);
        if ((long)width * height > options.MaxPixels)
        {
            return new ImageConversion.Rejected(ImageRejection.TooManyPixels);
        }

        if (width < options.MinDimension || height < options.MinDimension)
        {
            return new ImageConversion.Rejected(ImageRejection.Icon);
        }

        // ponytail: EXIF orientation is ignored; news sites deliver upright images, phone photos may appear rotated.
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return new ImageConversion.Rejected(ImageRejection.NotAnImage);
        }

        var scale = Math.Min(1f, (float)options.MaxDimension / Math.Max(width, height));
        while (true)
        {
            var size = new SKSizeI(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
            if (size.Width < options.MinDimension || size.Height < options.MinDimension)
            {
                return new ImageConversion.Rejected(ImageRejection.TooLarge);
            }

            using var gray = ToGrayscale(decoded, size);
            foreach (var quality in FallbackQualities.Prepend(options.Quality))
            {
                using var jpeg = gray.Encode(SKEncodedImageFormat.Jpeg, quality);
                if (jpeg.Size <= options.MaxEncodedBytes)
                {
                    return new ImageConversion.Converted(jpeg.ToArray(), size.Width, size.Height);
                }
            }

            scale *= ShrinkFactor;
        }
    }

    // Scales onto a white canvas, so transparent areas do not turn black in the JPEG, then keeps only luminance.
    // A single gray channel makes the JPEG about a third smaller and is all an e-ink screen can show.
    private static SKBitmap ToGrayscale(SKBitmap source, SKSizeI size)
    {
        using var color = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(color))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, new SKRect(0, 0, size.Width, size.Height), new SKSamplingOptions(SKCubicResampler.Mitchell), null);
        }

        return color.Copy(SKColorType.Gray8)
            ?? throw new InvalidOperationException("Skia could not convert the image to grayscale.");
    }
}
