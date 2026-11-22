using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Tests.Processing;

public class ArticleFilterTests
{
    [Theory]
    [InlineData("Liveticker Straubing Tigers - Eisbären Berlin")]
    [InlineData("Live-Ticker: Bundestag debattiert Haushalt")]
    [InlineData("Wahl in Bayern im Liveblog")]
    [InlineData("LIVE: Pressekonferenz der Polizei")]
    [InlineData("+++ Unwetter im Süden +++ Bahn stellt Verkehr ein")]
    public void Live_tickers_are_left_out_when_the_source_says_so(string title)
    {
        var source = Source(s => s.SkipLiveTickers = true);

        Assert.False(ArticleFilter.Admits(Article(title), source));
        Assert.True(ArticleFilter.Admits(Article(title), Source(_ => { })));
    }

    [Theory]
    [InlineData("Börsenticker: Dax schließt im Plus")] // a stock ticker is no live ticker
    [InlineData("Neue Live-Musik im Stadtpark")]
    public void Ordinary_titles_are_no_live_tickers(string title) =>
        Assert.True(ArticleFilter.Admits(Article(title), Source(s => s.SkipLiveTickers = true)));

    [Fact]
    public void Excluded_keywords_match_whole_words_in_title_or_teaser_ignoring_case()
    {
        var source = Source(s => s.ExcludeKeywords = ["Fußball", "Promi"]);

        Assert.False(ArticleFilter.Admits(Article("Bundesliga: FC gewinnt", "<p>Ein Abend voller <b>FUSSBALL</b> und Fußball.</p>"), source));
        Assert.False(ArticleFilter.Admits(Article("Promi-Hochzeit am See"), source));
        Assert.True(ArticleFilter.Admits(Article("Promis und Fußballer feiern"), source)); // other words, not the keywords
    }

    [Fact]
    public void Included_keywords_let_only_matching_articles_in()
    {
        var source = Source(s => s.IncludeKeywords = ["Proxmox", "Raspberry Pi"]);

        Assert.True(ArticleFilter.Admits(Article("Neuer Raspberry Pi vorgestellt"), source));
        Assert.True(ArticleFilter.Admits(Article("Kernel-Update", "<p>Auch für Proxmox.</p>"), source));
        Assert.False(ArticleFilter.Admits(Article("Neues Smartphone vorgestellt"), source));
    }

    [Fact]
    public void Exclusion_wins_over_inclusion()
    {
        var source = Source(s =>
        {
            s.IncludeKeywords = ["Raspberry Pi"];
            s.ExcludeKeywords = ["Anzeige"];
        });

        Assert.False(ArticleFilter.Admits(Article("Anzeige: Raspberry Pi zum Sonderpreis"), source));
    }

    [Fact]
    public void Too_short_articles_are_left_out_after_the_full_text()
    {
        var source = Source(s => s.MinWords = 5);

        Assert.False(ArticleFilter.IsLongEnough(Article("Kurz", content: "<p>Nur drei Wörter.</p>"), source));
        Assert.True(ArticleFilter.IsLongEnough(Article("Lang", content: "<p>Hier stehen <b>deutlich</b> mehr als fünf Wörter im Text.</p>"), source));
        Assert.True(ArticleFilter.IsLongEnough(Article("Kurz", content: "<p>Kurz.</p>"), Source(_ => { }))); // no minimum set
    }

    private static Source Source(Action<Source> configure)
    {
        var source = new Source { UserId = Guid.NewGuid(), Name = "Test", Url = "https://example.com/feed" };
        configure(source);
        return source;
    }

    private static FetchedArticle Article(string title, string? summary = null, string? content = null) =>
        new(null, title, new Uri("https://example.com/a"), null, null, summary, content);
}
