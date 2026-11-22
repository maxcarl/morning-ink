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

    [Theory]
    [InlineData("Anzeige: Glasfaser-Router mit Wi-Fi 7 zum Bestpreis", null, "https://example.com/a")] // golem's style
    [InlineData("[Anzeige] Die besten Deals der Woche", null, "https://example.com/a")]
    [InlineData("Sponsored: Cloud hosting made easy", null, "https://example.com/a")]
    [InlineData("So sparen Sie Strom (Werbung)", null, "https://example.com/a")]
    [InlineData("So gelingt die Migration", "Advertorial", "https://example.com/a")]
    [InlineData("So gelingt die Migration", null, "https://example.com/sponsored/migration")]
    [InlineData("So gelingt die Migration", null, "https://example.com/advertorial/migration.html")]
    public void Marked_ads_are_left_out_when_the_source_says_so(string title, string? category, string url)
    {
        var article = Article(title) with { Url = new Uri(url), Categories = category is null ? [] : [category] };

        Assert.False(ArticleFilter.Admits(article, Source(s => s.SkipAds = true)));
        Assert.True(ArticleFilter.Admits(article, Source(s => s.SkipAds = false)));
    }

    // The word alone is no mark: tech sites write about displays, ad blockers and advertising rules.
    [Theory]
    [InlineData("Neue Anzeige im Tacho zeigt Reichweite", "<p>Die Anzeige ist größer.</p>")]
    [InlineData("EU verbietet personalisierte Werbung für Kinder", null)]
    [InlineData("Firefox: Werbeblocker im Test", "<p>Anzeige: so sieht es aus</p>")]
    [InlineData("Sponsored-Content-Regeln: Was Influencer beachten müssen", null)]
    public void Articles_about_ads_are_kept(string title, string? summary) =>
        Assert.True(ArticleFilter.Admits(Article(title, summary), Source(s => s.SkipAds = true)));

    [Fact]
    public void Exclusion_wins_over_inclusion()
    {
        var source = Source(s =>
        {
            s.IncludeKeywords = ["Raspberry Pi"];
            s.ExcludeKeywords = ["Sonderpreis"];
        });

        Assert.False(ArticleFilter.Admits(Article("Raspberry Pi zum Sonderpreis"), source));
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
