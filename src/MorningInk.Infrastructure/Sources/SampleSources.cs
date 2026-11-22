namespace MorningInk.Infrastructure.Sources;

/// <summary>What a sample source is about; the setup wizard makes one section per topic.</summary>
public enum SampleTopic
{
    News,
    Tech,
}

/// <param name="Language">Two-letter language of the feed.</param>
/// <param name="Country">ISO country for national news, e.g. "AT"; null for feeds that suit every reader of the language.</param>
public sealed record SampleSource(string Name, string Url, string Language, SampleTopic Topic, string? Country = null);

/// <summary>Well-known feeds from several countries the setup wizard offers, so a first issue needs no searching.</summary>
/// <remarks>Checked by hand on 2026-11-22. Feeds move now and then; a broken one shows up as unavailable in the issue.</remarks>
public static class SampleSources
{
    private const string FallbackLanguage = "en";

    /// <summary>All samples; within a language, the first of a topic is the one suggested when no country matches.</summary>
    public static IReadOnlyList<SampleSource> All { get; } =
    [
        // International, in English: the fallback for languages without samples.
        new("Deutsche Welle", "https://rss.dw.com/rdf/rss-en-all", "en", SampleTopic.News),
        new("Al Jazeera", "https://www.aljazeera.com/xml/rss/all.xml", "en", SampleTopic.News),
        new("BBC News", "https://feeds.bbci.co.uk/news/rss.xml", "en", SampleTopic.News, "GB"),
        new("The Guardian", "https://www.theguardian.com/world/rss", "en", SampleTopic.News, "GB"),
        new("NPR", "https://feeds.npr.org/1001/rss.xml", "en", SampleTopic.News, "US"),
        new("Ars Technica", "https://feeds.arstechnica.com/arstechnica/index", "en", SampleTopic.Tech),
        new("The Verge", "https://www.theverge.com/rss/index.xml", "en", SampleTopic.Tech),
        new("TechCrunch", "https://techcrunch.com/feed/", "en", SampleTopic.Tech),

        new("tagesschau", "https://www.tagesschau.de/index~rss2.xml", "de", SampleTopic.News, "DE"),
        new("DER SPIEGEL", "https://www.spiegel.de/schlagzeilen/index.rss", "de", SampleTopic.News, "DE"),
        new("ZEIT ONLINE", "https://newsfeed.zeit.de/index", "de", SampleTopic.News, "DE"),
        new("ORF", "https://rss.orf.at/news.xml", "de", SampleTopic.News, "AT"),
        new("SRF News", "https://www.srf.ch/news/bnf/rss/1646", "de", SampleTopic.News, "CH"),
        new("NZZ", "https://www.nzz.ch/recent.rss", "de", SampleTopic.News, "CH"),
        new("heise online", "https://www.heise.de/rss/heise-atom.xml", "de", SampleTopic.Tech),
        new("Golem.de", "https://rss.golem.de/rss.php?feed=RSS2.0", "de", SampleTopic.Tech),

        new("Le Monde", "https://www.lemonde.fr/rss/une.xml", "fr", SampleTopic.News, "FR"),
        new("France 24", "https://www.france24.com/fr/rss", "fr", SampleTopic.News),
        new("Numerama", "https://www.numerama.com/feed/", "fr", SampleTopic.Tech),

        new("El País", "https://feeds.elpais.com/mrss-s/pages/ep/site/elpais.com/portada", "es", SampleTopic.News, "ES"),
        new("Xataka", "https://www.xataka.com/feedburner.xml", "es", SampleTopic.Tech),

        new("ANSA", "https://www.ansa.it/sito/notizie/topnews/topnews_rss.xml", "it", SampleTopic.News, "IT"),
        new("Wired Italia", "https://www.wired.it/feed/rss", "it", SampleTopic.Tech),

        new("NOS", "https://feeds.nos.nl/nosnieuwsalgemeen", "nl", SampleTopic.News, "NL"),
        new("NOS Tech", "https://feeds.nos.nl/nosnieuwstech", "nl", SampleTopic.Tech),

        new("g1", "https://g1.globo.com/rss/g1/", "pt", SampleTopic.News, "BR"),
        new("NHK", "https://www3.nhk.or.jp/rss/news/cat0.xml", "ja", SampleTopic.News, "JP"),
    ];

    /// <summary>One sample per topic for a first issue: from the user's country, else in their language, else international English.</summary>
    public static IReadOnlyList<SampleSource> Suggest(string language, string? country) =>
        Enum.GetValues<SampleTopic>()
            .Select(topic => All.Where(s => s.Topic == topic).ToList())
            .Select(candidates =>
                candidates.FirstOrDefault(s => country is not null && s.Country == country)
                ?? candidates.FirstOrDefault(s => s.Language == language && s.Country is null)
                ?? candidates.FirstOrDefault(s => s.Language == language)
                ?? candidates.First(s => s.Language == FallbackLanguage && s.Country is null))
            .ToList();
}
