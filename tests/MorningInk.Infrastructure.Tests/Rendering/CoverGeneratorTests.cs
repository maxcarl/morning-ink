using MorningInk.Infrastructure.Rendering;
using SkiaSharp;

namespace MorningInk.Infrastructure.Tests.Rendering;

public class CoverGeneratorTests
{
    private const string Long = "Eine außergewöhnlich lange Überschrift, die sich über viele Zeilen zieht und trotzdem auf der Titelseite Platz finden muss, ohne über den Rand zu laufen";

    [Fact]
    public void Full_front_page_fits_the_kindle_format()
    {
        var stories = Enumerable.Range(1, 12).Select(i => new CoverStory($"Rubrik {i}", i % 2 == 0 ? Long : $"Meldung {i}")).ToList();

        var cover = Decode(CoverGenerator.Create(new CoverPage("Die Frühstücksausgabe mit einem sehr langen Namen", "Samstag, 21. November 2026", "Ausgabe 42", new CoverStory("Politik", Long), stories, "Wetter für München: bewölkt, 1–7 °C", "13 Artikel in 4 Rubriken")));

        Assert.Equal((1600, 2560), (cover.Width, cover.Height));
        Assert.Equal(SKColorType.Gray8, cover.ColorType);
    }

    [Fact]
    public void One_story_without_weather_is_a_cover_too()
    {
        var cover = Decode(CoverGenerator.Create(new CoverPage("Morning Ink", "Saturday, November 21, 2026", "Issue 1", new CoverStory(null, "Only one story"), [], null, null)));

        Assert.Equal((1600, 2560), (cover.Width, cover.Height));
    }

    private static SKBitmap Decode(byte[] jpeg) => SKBitmap.Decode(jpeg) ?? throw new InvalidOperationException("Not a JPEG.");
}
