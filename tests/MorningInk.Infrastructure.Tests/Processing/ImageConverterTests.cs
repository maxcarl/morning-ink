using MorningInk.Infrastructure.Processing;
using SkiaSharp;

namespace MorningInk.Infrastructure.Tests.Processing;

public class ImageConverterTests
{
    private readonly ImageOptions _options = new();

    [Fact]
    public void Photos_become_small_grayscale_jpegs()
    {
        var result = Assert.IsType<ImageConversion.Converted>(ImageConverter.Convert(TestImages.Png(2000, 1500), _options));

        using var output = SKBitmap.Decode(result.Jpeg);
        Assert.Equal(SKEncodedImageFormat.Jpeg, SKCodec.Create(SKData.CreateCopy(result.Jpeg)).EncodedFormat);
        Assert.Equal((1000, 750), (output.Width, output.Height));
        var pixel = output.GetPixel(500, 375);
        Assert.True(pixel.Red == pixel.Green && pixel.Green == pixel.Blue, $"Not gray: {pixel}");
    }

    [Theory]
    [InlineData(32, 32)] // icon
    [InlineData(1, 1)] // tracking pixel
    [InlineData(600, 60)] // thin banner or divider
    public void Icons_and_tiny_images_are_dropped(int width, int height) =>
        Assert.Equal(ImageRejection.Icon, Assert.IsType<ImageConversion.Rejected>(ImageConverter.Convert(TestImages.Png(width, height), _options)).Reason);

    [Fact]
    public void Pixel_bombs_are_rejected_before_decoding() =>
        Assert.Equal(ImageRejection.TooManyPixels, Assert.IsType<ImageConversion.Rejected>(ImageConverter.Convert(TestImages.PixelBomb(30000, 30000), _options)).Reason);

    [Fact]
    public void Garbage_is_not_an_image() =>
        Assert.Equal(ImageRejection.NotAnImage, Assert.IsType<ImageConversion.Rejected>(ImageConverter.Convert("<svg onload=alert(1)>"u8.ToArray(), _options)).Reason);

    [Fact]
    public void Output_stays_under_the_size_limit_even_for_noisy_images()
    {
        var result = Assert.IsType<ImageConversion.Converted>(ImageConverter.Convert(TestImages.Png(1600, 1600, noise: true), _options));

        Assert.True(result.Jpeg.Length <= _options.MaxEncodedBytes, $"{result.Jpeg.Length} bytes");
    }

    [Fact]
    public void Transparency_becomes_white_not_black()
    {
        var result = Assert.IsType<ImageConversion.Converted>(ImageConverter.Convert(TestImages.Png(400, 400, alpha: 0), _options));

        using var output = SKBitmap.Decode(result.Jpeg);
        Assert.True(output.GetPixel(200, 200).Red > 245);
    }
}
