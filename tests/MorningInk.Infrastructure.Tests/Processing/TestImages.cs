using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace MorningInk.Infrastructure.Tests.Processing;

/// <summary>Images generated on the fly, so tests need no binary fixtures.</summary>
internal static class TestImages
{
    /// <summary>A colorful PNG; with <paramref name="noise"/> it compresses badly, like a photo.</summary>
    public static byte[] Png(int width, int height, bool noise = false, byte alpha = 255)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var random = new Random(42);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = noise
                    ? new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha)
                    : new SKColor((byte)(x * 255 / width), 40, (byte)(y * 255 / height), alpha);
                bitmap.SetPixel(x, y, color);
            }
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A tiny PNG whose header claims a huge size: decoding it would need gigabytes.</summary>
    public static byte[] PixelBomb(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00]);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc32(typeAndData));
        png.Write(number);
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
