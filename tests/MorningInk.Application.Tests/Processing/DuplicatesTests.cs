using MorningInk.Application.Processing;
using MorningInk.Application.Sources;

namespace MorningInk.Application.Tests.Processing;

public class DuplicatesTests
{
    [Theory]
    [InlineData("Großbrand in Schwabing: Wohnhaus evakuiert, Schule geschlossen", "Großbrand in Schwabing: Wohnhaus evakuiert, Schule ge…")] // cut off
    [InlineData("Großbrand in Schwabing: Wohnhaus evakuiert", "Schwabing: Wohnhaus nach Großbrand evakuiert")] // reordered
    [InlineData("FC Bayern gewinnt gegen Dortmund 3:1", "Bayern gewinnt gegen Dortmund mit 3:1 – FC Bayern jubelt")]
    public void Same_story_from_two_sources_is_one(string first, string second) =>
        Assert.True(Duplicates.AreSame(Article(first, "https://a.example/1"), Article(second, "https://b.example/2")));

    [Theory]
    [InlineData("Großbrand in Schwabing: Wohnhaus evakuiert", "Großbrand in Giesing: Lagerhalle zerstört")]
    [InlineData("Stadtrat beschließt neuen Haushalt", "Stadtrat streitet über Radwege")]
    [InlineData("Wetter", "Wetter")] // too few words to tell
    public void Different_stories_stay_apart(string first, string second) =>
        Assert.False(Duplicates.AreSame(Article(first, "https://a.example/1"), Article(second, "https://b.example/2")));

    [Fact]
    public void Same_address_is_the_same_story_whatever_the_titles() =>
        Assert.True(Duplicates.AreSame(
            Article("Erste Fassung", "https://example.com/artikel?utm_source=rss"),
            Article("Ganz andere Überschrift", "https://example.com/artikel")));

    private static FetchedArticle Article(string title, string url) => new(null, title, new Uri(url), null, null, null, null);
}
