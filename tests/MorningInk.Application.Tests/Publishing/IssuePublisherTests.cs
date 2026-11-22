using MorningInk.Application.Delivery;
using MorningInk.Application.Publishing;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Tests.Publishing;

public sealed class IssuePublisherTests : IDisposable
{
    private static readonly DateTimeOffset Slot = new(2026, 11, 21, 5, 0, 0, TimeSpan.Zero); // 6:00 in Berlin

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Source _tagesschau;
    private readonly Source _heise;
    private readonly Profile _profile;
    private readonly FakeStore _store;
    private readonly FakeSource _sources = new();
    private readonly FakeSeen _seen = new();
    private readonly FakeRenderer _renderer = new();
    private readonly FakeDelivery _delivery = new();
    private readonly TempIssueFiles _files = new();
    private readonly ProfileLocks _locks = new();
    private readonly FakeGeocoder _geocoder = new();
    private readonly FakeWeather _weather = new();

    public IssuePublisherTests()
    {
        _tagesschau = new Source { UserId = _userId, Name = "tagesschau", Url = "https://tagesschau.de/rss" };
        _heise = new Source { UserId = _userId, Name = "heise", Url = "https://heise.de/rss" };
        _profile = new Profile { UserId = _userId, Title = "Morning Ink", Recipient = "max@kindle.com" };
        _profile.Sections.Add(new Section { Name = "Nachrichten", Position = 1, Sources = { _tagesschau } });
        _profile.Sections.Add(new Section { Name = "Tech", Position = 2, Sources = { _heise } });
        _store = new FakeStore(_profile);
        Answer(_tagesschau, Articles("https://tagesschau.de/a", "https://tagesschau.de/b"));
        Answer(_heise, Articles("https://heise.de/x"));
    }

    [Fact]
    public async Task Failed_source_still_delivers_the_issue_with_a_note()
    {
        Answer(_heise, new SourceResult.Failure("heise.de could not be reached."));

        var result = await PublishAsync();

        var delivered = Assert.IsType<PublishResult.Delivered>(result);
        Assert.Equal(2, delivered.Articles);
        Assert.Equal("heise", Assert.Single(delivered.UnavailableSources).SourceName);
        var content = Assert.Single(_renderer.Rendered);
        Assert.Equal(["heise"], content.UnavailableSources);
        Assert.Single(_delivery.Requests);
        Assert.Equal(["heise: heise.de could not be reached."], Assert.Single(_store.Issues).Problems); // for the dashboard
    }

    [Fact]
    public async Task Source_that_throws_counts_as_unavailable()
    {
        _sources.Results.Remove(_heise.Id);

        var result = await PublishAsync();

        Assert.Equal("heise", Assert.Single(Assert.IsType<PublishResult.Delivered>(result).UnavailableSources).SourceName);
    }

    [Fact]
    public async Task An_issue_is_never_sent_twice()
    {
        var first = Assert.IsType<PublishResult.Delivered>(await PublishAsync());

        var second = await PublishAsync();

        Assert.Equal(first.IssueId, Assert.IsType<PublishResult.AlreadyDelivered>(second).IssueId);
        Assert.Single(_delivery.Requests);
    }

    [Fact]
    public async Task Failed_delivery_is_retried_with_the_same_file()
    {
        _delivery.Results.Enqueue(new DeliveryResult.Failure("Server down", Retryable: true));

        var failed = Assert.IsType<PublishResult.DeliveryFailed>(await PublishAsync());
        var retried = await PublishAsync();

        Assert.True(failed.Retryable);
        Assert.Equal(1, failed.Attempts);
        Assert.IsType<PublishResult.Delivered>(retried);
        Assert.Single(_renderer.Rendered);
        Assert.Equal(2, _delivery.Requests.Count);
        Assert.Equal(_delivery.Requests[0].FilePath, _delivery.Requests[1].FilePath);
        Assert.Equal([false, true], _store.Issues.Single().Attempts.Select(a => a.Succeeded));
    }

    [Fact]
    public async Task Articles_count_as_seen_only_after_delivery()
    {
        _delivery.Results.Enqueue(new DeliveryResult.Failure("Server down", Retryable: true));

        await PublishAsync();
        Assert.Empty(_seen.Seen);

        await PublishAsync();
        Assert.Equal(3, _seen.Seen.Count);
    }

    [Fact]
    public async Task Next_slot_gets_the_next_number_and_only_new_articles()
    {
        await PublishAsync();
        Answer(_tagesschau, Articles("https://tagesschau.de/a", "https://tagesschau.de/c"));

        var next = Assert.IsType<PublishResult.Delivered>(await PublishAsync(Slot.AddDays(1)));

        Assert.Equal(1, next.Articles);
        Assert.Equal([1, 2], _store.Issues.Select(i => i.Number));
        Assert.Equal(2, _renderer.Rendered[1].Number);
    }

    [Fact]
    public async Task Concurrent_run_for_the_same_profile_returns_at_once()
    {
        Assert.True(_locks.TryEnter(_profile.Id, out var running));
        using (running)
        {
            Assert.IsType<PublishResult.AlreadyRunning>(await PublishAsync());
        }

        Assert.Empty(_delivery.Requests);
    }

    [Fact]
    public async Task Nothing_is_sent_without_new_articles()
    {
        await PublishAsync();

        var result = await PublishAsync(Slot.AddDays(1));

        Assert.IsType<PublishResult.NoNewArticles>(result);
        Assert.Single(_delivery.Requests);
    }

    [Fact]
    public async Task Article_from_a_source_in_two_sections_appears_once()
    {
        _profile.Sections.Last().Sources.Add(_tagesschau);

        var result = Assert.IsType<PublishResult.Delivered>(await PublishAsync());

        Assert.Equal(3, result.Articles);
        Assert.Single(_sources.Fetched, s => s.Id == _tagesschau.Id);
    }

    [Fact]
    public async Task Every_article_is_processed_and_a_failing_one_is_left_out()
    {
        Answer(_heise, Articles("https://heise.de/x", "https://heise.de/crash"));

        await PublishAsync();

        var articles = _renderer.Rendered.Single().Sections.SelectMany(s => s.Articles).ToList();
        Assert.Equal(3, articles.Count);
        Assert.All(articles, a => Assert.EndsWith("[processed]", a.Article.Content, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sections_keep_their_order_limit_and_newest_first()
    {
        _profile.Sections.First().MaxArticles = 1;

        await PublishAsync();

        var sections = _renderer.Rendered.Single().Sections;
        Assert.Equal(["Nachrichten", "Tech"], sections.Select(s => s.Name));
        Assert.Equal("https://tagesschau.de/a", Assert.Single(sections[0].Articles).Article.Url.AbsoluteUri);
    }

    [Fact]
    public async Task Cache_validators_are_kept_on_the_source()
    {
        _sources.Results[_heise.Id] = () => new SourceResult.Success(Articles("https://heise.de/x").Articles, "\"v7\"", null);

        await PublishAsync();

        Assert.Equal("\"v7\"", _heise.ETag);
    }

    [Fact]
    public async Task Issue_uses_the_profile_time_zone_and_language_and_goes_to_the_recipient()
    {
        await PublishAsync();

        var content = _renderer.Rendered.Single();
        Assert.Equal(TimeSpan.FromHours(1), content.PublishedAt.Offset);
        Assert.Equal("de", content.Language);
        var request = Assert.Single(_delivery.Requests);
        Assert.Equal("max@kindle.com", request.Recipient);
        Assert.Equal("Morning Ink 2026-11-21", request.Title);
        Assert.True(File.Exists(request.FilePath));
    }

    [Fact]
    public async Task Weather_for_the_region_comes_with_the_issue()
    {
        _profile.Region = "10115";

        await PublishAsync();

        Assert.Same(_weather.Forecast, _renderer.Rendered.Single().Weather);
        Assert.Equal("Berlin", _profile.Location?.Name);
    }

    [Fact]
    public async Task Region_is_geocoded_once_and_again_only_when_it_changes()
    {
        _profile.Region = "10115";

        await PublishAsync();
        Answer(_tagesschau, Articles("https://tagesschau.de/day2"));
        await PublishAsync(Slot.AddDays(1));
        _profile.Region = "52062";
        Answer(_tagesschau, Articles("https://tagesschau.de/day3"));
        await PublishAsync(Slot.AddDays(2));
        _profile.Country = "DE"; // same postal code, now in a country: looked up again
        Answer(_tagesschau, Articles("https://tagesschau.de/day4"));
        await PublishAsync(Slot.AddDays(3));

        Assert.Equal(["10115", "52062", "52062 (DE)"], _geocoder.Queries);
    }

    [Fact]
    public async Task Issue_comes_without_weather_when_the_service_is_down()
    {
        _profile.Region = "10115";
        _weather.Fail = true;

        var result = await PublishAsync();

        Assert.IsType<PublishResult.Delivered>(result);
        Assert.Null(_renderer.Rendered.Single().Weather);
    }

    [Fact]
    public async Task Without_a_region_there_is_no_weather_and_no_lookup()
    {
        await PublishAsync();

        Assert.Empty(_geocoder.Queries);
        Assert.Null(_renderer.Rendered.Single().Weather);
    }

    [Fact]
    public async Task Unknown_region_leaves_the_weather_out()
    {
        _profile.Region = "unknown";

        await PublishAsync();

        Assert.Null(_profile.Location);
        Assert.Null(_renderer.Rendered.Single().Weather);
    }

    [Fact]
    public async Task Filtered_articles_take_no_place_in_the_section_limit()
    {
        _profile.Sections.Single(s => s.Name == "Nachrichten").MaxArticles = 1;
        _tagesschau.ExcludeKeywords = ["Liveticker"];
        Answer(_tagesschau, new SourceResult.Success(
            [
                new FetchedArticle(null, "Liveticker zur Wahl", new Uri("https://tagesschau.de/live"), Slot.AddHours(-1), null, null, "<p>Text</p>"),
                new FetchedArticle(null, "Ergebnis der Wahl", new Uri("https://tagesschau.de/wahl"), Slot.AddHours(-2), null, null, "<p>Text</p>"),
            ],
            ETag: null,
            LastModified: null));

        await PublishAsync();

        var news = Assert.Single(_renderer.Rendered).Sections.Single(s => s.Name == "Nachrichten");
        Assert.Equal("Ergebnis der Wahl", Assert.Single(news.Articles).Article.Title);
    }

    [Fact]
    public async Task Articles_below_the_minimum_length_are_left_out()
    {
        _heise.MinWords = 50; // the articles here have one word of text

        await PublishAsync();

        Assert.DoesNotContain(Assert.Single(_renderer.Rendered).Sections, s => s.Name == "Tech");
    }

    [Fact]
    public async Task Same_story_from_two_sources_appears_once_with_the_other_source_named()
    {
        Answer(_tagesschau, new SourceResult.Success(
            [new FetchedArticle(null, "Großbrand in Schwabing: Wohnhaus evakuiert", new Uri("https://tagesschau.de/brand"), Slot.AddHours(-1), null, null, "<p>Text</p>")],
            ETag: null,
            LastModified: null));
        Answer(_heise, new SourceResult.Success(
            [new FetchedArticle(null, "Schwabing: Wohnhaus nach Großbrand evakuiert", new Uri("https://heise.de/brand"), Slot.AddHours(-2), null, null, "<p>Text</p>")],
            ETag: null,
            LastModified: null));

        var result = await PublishAsync();

        var sections = Assert.Single(_renderer.Rendered).Sections;
        var kept = Assert.Single(Assert.Single(sections).Articles); // the Tech section had nothing else and is gone
        Assert.Equal(("tagesschau", "heise"), (kept.SourceName, Assert.Single(kept.AlsoAt).SourceName));
        Assert.Equal(1, Assert.IsType<PublishResult.Delivered>(result).Articles);
        Assert.Contains("https://heise.de/brand", _seen.Seen); // so it does not come alone tomorrow
    }

    [Fact]
    public async Task Issue_prepared_ahead_is_sent_at_its_time_without_being_built_again()
    {
        var prepared = await CreatePublisher().PublishAsync(_profile.Id, Slot, CancellationToken.None, prepareOnly: true);

        Assert.IsType<PublishResult.Prepared>(prepared);
        Assert.Single(_renderer.Rendered);
        Assert.Empty(_delivery.Requests); // nothing goes out before the delivery time
        var fetchedWhilePreparing = _sources.Fetched.Count;

        var sent = await PublishAsync();

        Assert.IsType<PublishResult.Delivered>(sent);
        Assert.Single(_renderer.Rendered);
        Assert.Single(_delivery.Requests);
        Assert.Equal(fetchedWhilePreparing, _sources.Fetched.Count); // the sources were not asked again
    }

    [Fact]
    public async Task Preview_shows_the_next_issue_but_stores_sends_and_marks_nothing()
    {
        _tagesschau.ETag = "\"v1\""; // as after an earlier fetch

        var preview = await CreatePublisher().PreviewAsync(_profile.Id, withAi: false, CancellationToken.None);

        var built = Assert.IsType<PreviewResult.Built>(preview);
        Assert.NotEmpty(built.File);
        Assert.Empty(_store.Issues);
        Assert.Empty(_delivery.Requests);
        Assert.Empty(_seen.Seen);
        Assert.All(_sources.Fetched, s => Assert.Null(s.ETag)); // asked afresh, or an unchanged feed would look empty
        Assert.All(Assert.Single(_renderer.Rendered).Sections.SelectMany(s => s.Articles), a => Assert.Null(a.Article.AiSummary));
    }

    [Fact]
    public async Task Preview_runs_the_ai_only_when_asked()
    {
        var preview = await CreatePublisher().PreviewAsync(_profile.Id, withAi: true, CancellationToken.None);

        Assert.IsType<PreviewResult.Built>(preview);
        Assert.All(Assert.Single(_renderer.Rendered).Sections.SelectMany(s => s.Articles), a => Assert.Equal("KI", a.Article.AiSummary));
    }

    [Fact]
    public async Task Interests_decide_which_articles_fit_into_a_tight_section()
    {
        _profile.Sections.Single(s => s.Name == "Nachrichten").MaxArticles = 1;
        _profile.Interests = [new Interest { Topic = "Raumfahrt", Keywords = ["Raumfahrt"], Weight = InterestWeight.Prefer }];
        Answer(_tagesschau, new SourceResult.Success(
            [
                new FetchedArticle(null, "Neues vom Wetter", new Uri("https://tagesschau.de/wetter"), Slot.AddHours(-1), null, null, "<p>Text</p>"),
                new FetchedArticle(null, "Raumfahrt: Start geglückt", new Uri("https://tagesschau.de/start"), Slot.AddHours(-3), null, null, "<p>Text</p>"),
            ],
            ETag: null,
            LastModified: null));

        await PublishAsync();

        var news = Assert.Single(_renderer.Rendered).Sections.Single(s => s.Name == "Nachrichten");
        Assert.Equal("Raumfahrt: Start geglückt", Assert.Single(news.Articles).Article.Title); // older, but of interest
    }

    [Fact]
    public async Task Unknown_profile_is_reported() =>
        Assert.IsType<PublishResult.ProfileNotFound>(await CreatePublisher().PublishAsync(Guid.NewGuid(), Slot, CancellationToken.None));

    public void Dispose() => _files.Dispose();

    private void Answer(Source source, SourceResult result) => _sources.Results[source.Id] = () => result;

    // Newest first, one hour apart, like a feed delivers them.
    private static SourceResult.Success Articles(params string[] urls) => new(
        urls.Select((url, i) => new FetchedArticle(null, url, new Uri(url), Slot.AddHours(-1 - i), null, null, "<p>Text</p>")).ToList(),
        ETag: null,
        LastModified: null);

    private Task<PublishResult> PublishAsync(DateTimeOffset? slot = null) =>
        CreatePublisher().PublishAsync(_profile.Id, slot ?? Slot, CancellationToken.None);

    private IssuePublisher CreatePublisher() => new(
        _store, [_sources], _seen, [new MarkingProcessor(), new FakeAiProcessor()], [_renderer], _delivery, _files, _locks, _geocoder, _weather, new FixedTimeProvider(Slot));
}
