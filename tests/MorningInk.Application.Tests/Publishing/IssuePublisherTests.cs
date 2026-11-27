using MorningInk.Application.Delivery;
using MorningInk.Application.Publishing;
using MorningInk.Application.Rendering;
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
    private readonly FakeOnThisDay _onThisDay = new();
    private readonly FakeFeaturedArticle _featured = new();
    private readonly FakeNotifier _notifier = new();
    private readonly FakeOverview _overview = new();
    private readonly FakeHeadlines _headlines = new();
    private readonly FakeInterestMatcher _matcher = new();

    public IssuePublisherTests()
    {
        _tagesschau = new Source { UserId = _userId, Name = "tagesschau", Url = "https://tagesschau.de/rss" };
        _heise = new Source { UserId = _userId, Name = "heise", Url = "https://heise.de/rss" };
        _profile = new Profile { UserId = _userId, Title = "Morning Ink", Recipient = "alex@kindle.com" };
        _profile.Sections.Add(new Section { Name = "Nachrichten", Position = 1, Sources = { _tagesschau } });
        _profile.Sections.Add(new Section { Name = "Tech", Position = 2, Sources = { _heise } });
        _store = new FakeStore(_profile);
        Answer(_tagesschau, Articles("https://tagesschau.de/a", "https://tagesschau.de/b"));
        Answer(_heise, Articles("https://heise.de/x"));
    }

    [Fact]
    public async Task Delivered_articles_are_reported_back_to_their_source_by_its_own_ids()
    {
        await PublishAsync();

        var tagesschau = Assert.Single(_sources.Acknowledged, a => a.SourceId == _tagesschau.Id);
        Assert.Equal(["id:https://tagesschau.de/a", "id:https://tagesschau.de/b"], tagesschau.ArticleIds.Order());
        Assert.Equal(["id:https://heise.de/x"], Assert.Single(_sources.Acknowledged, a => a.SourceId == _heise.Id).ArticleIds);
        Assert.Contains(Assert.Single(_store.Issues).Log, e => e.Event == "MarkedRead" && e.Arguments.SequenceEqual(["tagesschau", "2"]));
    }

    [Fact]
    public async Task Nothing_is_reported_back_before_the_issue_is_delivered()
    {
        _delivery.Results.Enqueue(new DeliveryResult.Failure("Server down", Retryable: true));

        await PublishAsync();

        Assert.Empty(_sources.Acknowledged); // FreshRSS would show them read although they never arrived
    }

    [Fact]
    public async Task A_source_that_cannot_be_told_never_costs_the_issue()
    {
        _sources.AcknowledgeProblem = "freshrss.lan could not be reached.";

        var result = await PublishAsync();

        Assert.IsType<PublishResult.Delivered>(result);
        Assert.Contains(Assert.Single(_store.Issues).Log, e => e.Event == "MarkReadFailed" && e.Arguments[0] == "heise");
    }

    [Fact]
    public async Task Failed_source_still_delivers_the_issue_with_a_note()
    {
        Answer(_heise, new SourceResult.Failure("heise.de could not be reached.", Reason: "Unreachable", Arguments: ["heise.de"]));

        var result = await PublishAsync();

        var delivered = Assert.IsType<PublishResult.Delivered>(result);
        Assert.Equal(2, delivered.Articles);
        Assert.Equal("heise", Assert.Single(delivered.UnavailableSources).SourceName);
        var content = Assert.Single(_renderer.Rendered);
        Assert.Equal(["heise"], content.UnavailableSources);
        Assert.Single(_delivery.Requests);
        // The dashboard reads it from the log: source, English text, then code and values for the user's language.
        var failed = Assert.Single(Assert.Single(_store.Issues).Log, e => e.Event == "SourceFailed");
        Assert.Equal(["heise", "heise.de could not be reached.", "Unreachable", "heise.de"], failed.Arguments);
    }

    [Fact]
    public async Task Unchanged_source_adds_nothing_and_keeps_its_validators()
    {
        _heise.ETag = "\"v1\"";
        Answer(_heise, new SourceResult.NotModified());

        var result = await PublishAsync();

        var delivered = Assert.IsType<PublishResult.Delivered>(result);
        Assert.Equal(2, delivered.Articles);
        Assert.Empty(delivered.UnavailableSources);
        Assert.Equal("\"v1\"", _heise.ETag);
        Assert.IsType<SourceHealth.Empty>(_heise.Health(Slot)); // nothing new, not failing

    }

    [Fact]
    public async Task Each_run_keeps_track_of_whether_a_source_works()
    {
        Answer(_heise, new SourceResult.Failure("heise.de could not be reached.", Reason: "Unreachable", Arguments: ["heise.de"]));

        await PublishAsync();

        Assert.Equal(new SourceHealth.Failing(Slot, Warning: false), _heise.Health(Slot));
        Assert.Equal("Unreachable", _heise.LastProblemReason);
        Assert.Equal(new SourceHealth.Fine(), _tagesschau.Health(Slot));
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
    public async Task Too_few_articles_skip_the_issue_and_keep_them_for_the_next()
    {
        _profile.MinArticles = 4; // three are fresh

        var result = await PublishAsync();

        Assert.IsType<PublishResult.NoNewArticles>(result);
        Assert.Empty(_delivery.Requests);
        Assert.Contains(Assert.Single(_store.Issues).Log, e => e.Event == "TooFewArticles" && e.Arguments.SequenceEqual(["3", "4"]));

        _profile.MinArticles = 3;
        Assert.IsType<PublishResult.Delivered>(await PublishAsync(Slot.AddDays(1))); // not marked seen, so they come again
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
        Assert.Equal("alex@kindle.com", request.Recipient);
        Assert.Equal("Morning Ink 2026-11-21", request.Title);
        Assert.True(File.Exists(request.FilePath));
    }

    [Fact]
    public async Task The_overview_stands_where_its_section_is_and_points_at_articles_of_the_issue()
    {
        _profile.Sections.Add(new Section { Name = "Das Wichtigste", Position = 2, Kind = SectionKind.Overview }); // between the two
        Reorder("Nachrichten", "Das Wichtigste", "Tech");

        await PublishAsync();

        var content = _renderer.Rendered.Single();
        var overview = Assert.Single(content.Extras);
        Assert.Equal((IssueExtraKind.Overview, "Das Wichtigste", 1), (overview.Kind, overview.Title, overview.Before)); // after the first section
        Assert.All(overview.Overview!.Articles, article => Assert.Contains(content.Sections.SelectMany(s => s.Articles), a => ReferenceEquals(a, article)));
        Assert.Equal(3, Assert.Single(_overview.Requests).Articles.Count); // every article of the issue was offered
        Assert.Contains(Assert.Single(_store.Issues).Log, e => e.Event == "Overview" && e.Arguments[0] == "2");
    }

    [Fact]
    public async Task Without_ai_the_issue_comes_without_its_overview()
    {
        _overview.Available = false;
        _profile.Sections.Add(new Section { Name = "Das Wichtigste", Position = 0, Kind = SectionKind.Overview });

        var result = await PublishAsync();

        Assert.IsType<PublishResult.Delivered>(result);
        Assert.Empty(_renderer.Rendered.Single().Extras);
    }

    [Fact]
    public async Task The_preview_without_ai_asks_for_no_overview()
    {
        _profile.Sections.Add(new Section { Name = "Das Wichtigste", Position = 0, Kind = SectionKind.Overview });

        await CreatePublisher().PreviewAsync(_profile.Id, withAi: false, CancellationToken.None);

        Assert.Empty(_overview.Requests);
    }

    [Fact]
    public async Task A_section_switched_off_is_left_out_and_its_sources_are_not_even_asked()
    {
        _profile.Sections.Single(s => s.Name == "Tech").Enabled = false;

        await PublishAsync();

        Assert.Equal(["Nachrichten"], _renderer.Rendered.Single().Sections.Select(s => s.Name));
        Assert.DoesNotContain(_store.Issues.Single().Log, e => e.Event is "Fetched" or "SourceFailed" && e.Arguments[0] == "heise");
    }

    [Fact]
    public async Task Weather_further_down_gets_a_page_at_its_place()
    {
        _profile.Region = "10115";
        _profile.Sections.Add(new Section { Name = "Wetter", Position = 3, Kind = SectionKind.Weather });

        await PublishAsync();

        var content = _renderer.Rendered.Single();
        var placed = Assert.Single(content.Extras);
        Assert.Equal((IssueExtraKind.Weather, 2), (placed.Kind, placed.Before)); // after both sections
        Assert.Same(_weather.Forecast, content.Weather);
    }

    [Fact]
    public async Task On_this_day_at_the_end_brings_the_events_of_the_local_date_in_the_editions_country()
    {
        _profile.Country = "AT";
        _profile.Sections.Add(new Section { Name = "An diesem Tag", Position = 3, Kind = SectionKind.OnThisDay });

        await PublishAsync(new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.Zero)); // already the 22nd in Berlin

        var placed = Assert.Single(_renderer.Rendered.Single().Extras);
        Assert.Equal((IssueExtraKind.OnThisDay, "An diesem Tag", 2), (placed.Kind, placed.Title, placed.Before));
        Assert.Same(_onThisDay.Events, placed.Events);
        Assert.Equal((new DateOnly(2026, 11, 22), "AT"), _onThisDay.Requests.Single());
    }

    [Fact]
    public async Task On_this_day_switched_off_is_not_even_fetched()
    {
        _profile.Sections.Add(new Section { Name = "An diesem Tag", Position = 3, Kind = SectionKind.OnThisDay, Enabled = false });

        await PublishAsync();

        Assert.Empty(_renderer.Rendered.Single().Extras);
        Assert.Empty(_onThisDay.Requests);
    }

    [Fact]
    public async Task On_this_day_failing_leaves_the_issue_without_it()
    {
        _onThisDay.Fail = true;
        _profile.Sections.Add(new Section { Name = "An diesem Tag", Position = 3, Kind = SectionKind.OnThisDay });

        var result = await PublishAsync();

        Assert.IsType<PublishResult.Delivered>(result);
        Assert.Empty(_renderer.Rendered.Single().Extras);
    }

    [Fact]
    public async Task On_this_day_can_bring_the_article_of_the_day_instead_of_the_events()
    {
        _profile.Country = "AT";
        _profile.Sections.Add(new Section { Name = "An diesem Tag", Position = 3, Kind = SectionKind.OnThisDay, ShowAnniversaries = false, ShowFeaturedArticle = true });

        await PublishAsync(new DateTimeOffset(2026, 11, 21, 23, 30, 0, TimeSpan.Zero)); // already the 22nd in Berlin

        var placed = Assert.Single(_renderer.Rendered.Single().Extras);
        Assert.Equal((IssueExtraKind.OnThisDay, 2), (placed.Kind, placed.Before));
        Assert.Same(_featured.Article, placed.Featured);
        Assert.Null(placed.Events);
        Assert.Equal((new DateOnly(2026, 11, 22), "AT"), _featured.Requests.Single());
        Assert.Empty(_onThisDay.Requests); // switched off, not even fetched
    }

    [Fact]
    public async Task On_this_day_keeps_the_events_when_the_article_of_the_day_fails()
    {
        _featured.Fail = true;
        _profile.Sections.Add(new Section { Name = "An diesem Tag", Position = 3, Kind = SectionKind.OnThisDay, ShowFeaturedArticle = true });

        var result = await PublishAsync();

        Assert.IsType<PublishResult.Delivered>(result);
        var placed = Assert.Single(_renderer.Rendered.Single().Extras);
        Assert.Same(_onThisDay.Events, placed.Events);
        Assert.Null(placed.Featured);
    }

    [Fact]
    public async Task On_this_day_with_both_parts_off_is_left_out()
    {
        _profile.Sections.Add(new Section { Name = "An diesem Tag", Position = 3, Kind = SectionKind.OnThisDay, ShowAnniversaries = false });

        await PublishAsync();

        Assert.Empty(_renderer.Rendered.Single().Extras);
        Assert.Empty(_onThisDay.Requests);
    }

    [Fact]
    public async Task Article_of_the_day_is_not_fetched_unless_switched_on()
    {
        _profile.Sections.Add(new Section { Name = "An diesem Tag", Position = 3, Kind = SectionKind.OnThisDay });

        await PublishAsync();

        Assert.Empty(_featured.Requests);
    }

    [Fact]
    public async Task Weather_switched_off_is_not_even_fetched()
    {
        _profile.Region = "10115";
        _profile.Sections.Add(new Section { Name = "Wetter", Position = 0, Kind = SectionKind.Weather, Enabled = false });

        await PublishAsync();

        var content = _renderer.Rendered.Single();
        Assert.Null(content.Weather);
        Assert.Empty(content.Extras);
        Assert.Null(_profile.Location); // not even geocoded
    }

    [Fact]
    public async Task Every_article_carries_feedback_links_for_its_own_entry_in_the_issue()
    {
        await PublishAsync();

        var content = _renderer.Rendered.Single();
        var issue = _store.Issues.Single();
        foreach (var article in content.Sections.SelectMany(s => s.Articles))
        {
            var entry = issue.Entries.Single(e => e.Url == article.Article.Url.AbsoluteUri);
            Assert.Equal($"https://news.example.com/feedback/more-{entry.Id}", content.Feedback![article].More.AbsoluteUri);
        }
    }

    [Fact]
    public async Task Sources_the_reader_wants_more_of_come_first_within_a_section()
    {
        var golem = new Source { UserId = _userId, Name = "golem", Url = "https://golem.de/rss" };
        _profile.Sections.Single(s => s.Name == "Tech").Sources.Add(golem);
        Answer(golem, Articles("https://golem.de/older")); // older than heise's article
        _store.Leanings[golem.Id] = 2;
        _store.Leanings[_heise.Id] = -1;

        await PublishAsync();

        var tech = _renderer.Rendered.Single().Sections.Single(s => s.Name == "Tech");
        Assert.Equal(["golem", "heise"], tech.Articles.Select(a => a.SourceName));
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

        // Kept for the archive, with the keyword that decided, and not as an entry: it must not count as seen.
        var issue = Assert.Single(_store.Issues);
        var leftOut = Assert.Single(issue.LeftOut);
        Assert.Equal(("Nachrichten", "Liveticker zur Wahl", "https://tagesschau.de/live", "Excluded", "Liveticker", _tagesschau.Id), (leftOut.Section, leftOut.Title, leftOut.Url, leftOut.Reason, leftOut.Detail, leftOut.SourceId));
        Assert.DoesNotContain(issue.Entries, e => e.Url == leftOut.Url);
    }

    [Fact]
    public async Task Articles_below_the_minimum_length_are_left_out()
    {
        _heise.MinWords = 50; // the articles here have one word of text

        await PublishAsync();

        Assert.DoesNotContain(Assert.Single(_renderer.Rendered).Sections, s => s.Name == "Tech");
        Assert.NotEmpty(Assert.Single(_store.Issues).LeftOut);
        Assert.All(Assert.Single(_store.Issues).LeftOut, a => Assert.Equal(("Tech", "TooShort"), (a.Section, a.Reason))); // known only after the full text
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
    public async Task The_ai_recognizes_an_interest_without_its_keywords_and_the_log_names_it()
    {
        _profile.Interests = [new Interest { Topic = "Fußball", Keywords = ["Bundesliga"], Weight = InterestWeight.Hide }];
        _matcher.Topics["Kane"] = "Fußball";
        Answer(_tagesschau, Titled("Kane trifft doppelt für die Bayern", "Haushalt beschlossen"));

        await PublishAsync();

        var news = Assert.Single(_renderer.Rendered).Sections.Single(s => s.Name == "Nachrichten");
        Assert.Equal(["Haushalt beschlossen"], news.Articles.Select(a => a.Article.Title));
        var log = Assert.Single(_store.Issues).Log;
        Assert.Contains(log, e => e.Event == "InterestMatched" && e.Arguments.SequenceEqual(["Fußball", "1"]));
        Assert.Contains(log, e => e.Event == "InterestsRecognized" && e.Arguments[0] == "1");
        var leftOut = Assert.Single(Assert.Single(_store.Issues).LeftOut);
        Assert.Equal(("Kane trifft doppelt für die Bayern", "Interest", "Fußball"), (leftOut.Title, leftOut.Reason, leftOut.Detail));
    }

    [Fact]
    public async Task Without_ai_only_the_keywords_count()
    {
        _profile.Interests = [new Interest { Topic = "Fußball", Keywords = ["Bundesliga"], Weight = InterestWeight.Hide }];
        _matcher.Topics["Kane"] = "Fußball";
        _matcher.Available = false;
        Answer(_tagesschau, Titled("Kane trifft doppelt für die Bayern", "Bundesliga: Spieltag", "Haushalt beschlossen"));

        await PublishAsync();

        var news = Assert.Single(_renderer.Rendered).Sections.Single(s => s.Name == "Nachrichten");
        Assert.Equal(["Kane trifft doppelt für die Bayern", "Haushalt beschlossen"], news.Articles.Select(a => a.Article.Title));
        Assert.Contains(Assert.Single(_store.Issues).Log, e => e.Event == "InterestMatched" && e.Arguments.SequenceEqual(["Fußball", "1"]));
    }

    [Fact]
    public async Task The_preview_without_ai_does_not_ask_it_about_interests()
    {
        _profile.Interests = [new Interest { Topic = "Fußball", Keywords = ["Bundesliga"], Weight = InterestWeight.Hide }];

        await CreatePublisher().PreviewAsync(_profile.Id, withAi: false, CancellationToken.None);

        Assert.Empty(_matcher.Requests);
    }

    [Fact]
    public async Task Articles_like_the_ones_the_reader_wanted_more_of_come_first()
    {
        _store.Liked.Add("Rakete erreicht den Mond");
        _store.Disliked.Add("Promi heiratet");
        _matcher.Topics["Raumfahrt"] = "Similar to these articles the reader wanted more of";
        _profile.Sections.Single(s => s.Name == "Nachrichten").MaxArticles = 1;
        Answer(_tagesschau, Titled("Neues vom Wetter", "Raumfahrt: Start geglückt"));

        await PublishAsync();

        var liked = Assert.Single(_matcher.Requests).Interests.Single(i => i.Weight == InterestWeight.Prefer);
        Assert.Contains("\"Rakete erreicht den Mond\"", liked.Topic, StringComparison.Ordinal); // the titles go to the AI as examples
        Assert.Contains(_matcher.Requests[0].Interests, i => i.Weight == InterestWeight.Less && i.Topic.Contains("Promi heiratet", StringComparison.Ordinal));
        var news = Assert.Single(_renderer.Rendered).Sections.Single(s => s.Name == "Nachrichten");
        Assert.Equal("Raumfahrt: Start geglückt", Assert.Single(news.Articles).Article.Title); // older, but like a liked one
        Assert.Contains(_store.Issues.Single().Log, e => e.Event == "LikedMatched" && e.Arguments[0] == "1");
    }

    [Fact]
    public async Task A_failing_interest_step_costs_only_the_ai_sorting()
    {
        _profile.Interests = [new Interest { Topic = "Fußball", Keywords = ["Bundesliga"], Weight = InterestWeight.Hide }];
        _matcher.Throws = true;

        Assert.IsType<PublishResult.Delivered>(await PublishAsync());
    }

    [Fact]
    public async Task A_lurid_headline_is_replaced_and_the_original_kept_before_the_overview_quotes_it()
    {
        _profile.Sections.Add(new Section { Name = "Das Wichtigste", Position = 0, Kind = SectionKind.Overview });
        Answer(_tagesschau, Titled("Unglaublich, was in Berlin passiert", "Haushalt beschlossen"));

        await PublishAsync();

        var article = Assert.Single(_renderer.Rendered).Sections.SelectMany(s => s.Articles).Single(a => a.Article.OriginalTitle is not null).Article;
        Assert.Equal(("Sachlich: Unglaublich, was in Berlin passiert", "Unglaublich, was in Berlin passiert"), (article.Title, article.OriginalTitle));
        Assert.Contains(Assert.Single(_overview.Requests).Articles, a => a.Article.Article.Title.StartsWith("Sachlich", StringComparison.Ordinal));
        Assert.Contains(_store.Issues.Single().Log, e => e.Event == "Headlines" && e.Arguments[0] == "1");
    }

    [Fact]
    public async Task The_preview_without_ai_keeps_the_headlines()
    {
        Answer(_tagesschau, Titled("Unglaublich, was in Berlin passiert"));

        await CreatePublisher().PreviewAsync(_profile.Id, withAi: false, CancellationToken.None);

        Assert.All(Assert.Single(_renderer.Rendered).Sections.SelectMany(s => s.Articles), a => Assert.Null(a.Article.OriginalTitle));
    }

    [Fact]
    public async Task Building_an_issue_is_logged_step_by_step()
    {
        Answer(_heise, new SourceResult.Failure("heise.de could not be reached."));

        await PublishAsync();

        var log = Assert.Single(_store.Issues).Log;
        Assert.Contains(log, e => e.Event == "Fetched" && e.Arguments.SequenceEqual(["tagesschau", "2"]));
        Assert.Contains(log, e => e.Event == "SourceFailed" && e.Arguments[0] == "heise");
        Assert.Contains(log, e => e.Event == "Chosen" && e.Arguments[0] == "Nachrichten" && e.Arguments[1] == "2");
        Assert.Contains(log, e => e.Event == "Rendered");
        // Delivery ends the building; only what follows from it (sources told, notification) comes after.
        Assert.Equal("Delivered", log.Last(e => e.Event is not ("MarkedRead" or "MarkReadFailed" or "Notified" or "NotifyFailed")).Event);
        Assert.True(log.Zip(log.Skip(1)).All(pair => pair.First.At <= pair.Second.At)); // in the order it happened
    }

    [Fact]
    public async Task Delivered_issue_is_announced_where_the_edition_wants_it()
    {
        _profile.NotifyChannel = NotificationChannel.Ntfy;
        _profile.NotifyTarget = "https://ntfy.sh/morgenzeitung";

        await PublishAsync();

        var notice = Assert.Single(_notifier.Sent);
        Assert.Equal(("Morning Ink", 3, null), (notice.EditionTitle, notice.Articles, notice.Problem));
        Assert.Contains(_store.Issues.Single().Log, e => e.Event == "Notified");
    }

    [Fact]
    public async Task Failed_delivery_is_announced_once_not_at_every_retry()
    {
        _profile.NotifyChannel = NotificationChannel.Ntfy;
        _profile.NotifyTarget = "https://ntfy.sh/morgenzeitung";
        _delivery.Results.Enqueue(new DeliveryResult.Failure("down", Retryable: true));
        _delivery.Results.Enqueue(new DeliveryResult.Failure("down", Retryable: true));

        await PublishAsync();
        await PublishAsync();

        Assert.NotNull(Assert.Single(_notifier.Sent).Problem);
    }

    [Fact]
    public async Task A_failing_notification_never_costs_the_issue()
    {
        _profile.NotifyChannel = NotificationChannel.Ntfy;
        _profile.NotifyTarget = "https://ntfy.sh/morgenzeitung";
        _notifier.Throws = true;

        Assert.IsType<PublishResult.Delivered>(await PublishAsync());
        Assert.Contains(_store.Issues.Single().Log, e => e.Event == "NotifyFailed");
    }

    [Fact]
    public async Task An_edition_to_fetch_is_stored_without_a_mail_and_its_articles_count_as_seen()
    {
        _profile.SendByEmail = false;
        _profile.Recipient = string.Empty;

        var result = await PublishAsync();

        Assert.IsType<PublishResult.Delivered>(result);
        Assert.Empty(_delivery.Requests);
        var issue = Assert.Single(_store.Issues);
        Assert.Equal(IssueStatus.Delivered, issue.Status);
        Assert.Contains(issue.Log, e => e.Event == "Stored");
        Assert.DoesNotContain(issue.Log, e => e.Event == "Delivered");
        Assert.Contains("https://heise.de/x", _seen.Seen); // the reader fetches it: tomorrow brings new articles
    }

    [Fact]
    public async Task A_run_reports_its_steps_in_order_and_the_lock_knows_the_current_one()
    {
        var steps = new List<IssueStep>();
        var seenWhileRunning = new List<IssueStep?>();
        var progress = new InlineProgress(step =>
        {
            steps.Add(step);
            seenWhileRunning.Add(_locks.StepOf(_profile.Id));
        });

        await CreatePublisher().PublishAsync(_profile.Id, Slot, CancellationToken.None, progress: progress);

        Assert.Equal(
            [IssueStage.Fetching, IssueStage.Interests, IssueStage.Processing, IssueStage.Headlines, IssueStage.Extras, IssueStage.Rendering, IssueStage.Sending],
            steps.Select(s => s.Stage).Distinct());
        Assert.Equal([(0, 2), (1, 2)], steps.Where(s => s.Stage == IssueStage.Fetching).Select(s => (s.Done, s.Total))); // two sources
        Assert.Equal(steps.Cast<IssueStep?>(), seenWhileRunning); // the status page reads the same step
        Assert.Null(_locks.StepOf(_profile.Id)); // nothing runs any more
    }

    [Fact]
    public async Task Cancelled_build_stops_and_its_slot_is_not_built_again()
    {
        // Cancelled while a slow source is asked; a real fetch then throws, as this one does.
        _sources.Results[_heise.Id] = () =>
        {
            Assert.True(_locks.Cancel(_profile.Id));
            throw new OperationCanceledException();
        };

        Assert.IsType<PublishResult.Cancelled>(await PublishAsync());
        Answer(_heise, Articles("https://heise.de/x"));
        Assert.IsType<PublishResult.Cancelled>(await PublishAsync()); // the delivery trigger or a restart comes later

        var issue = Assert.Single(_store.Issues);
        Assert.Equal(IssueStatus.Cancelled, issue.Status);
        Assert.Contains(issue.Log, e => e.Event == "Cancelled");
        Assert.Empty(issue.Entries);
        Assert.Empty(_delivery.Requests);
        Assert.Empty(_seen.Seen);
    }

    [Fact]
    public async Task Issue_left_building_without_a_run_is_cancelled_directly()
    {
        var left = new Issue { ProfileId = _profile.Id, Number = 7, ScheduledFor = Slot, CreatedAt = Slot };
        _store.AddIssue(left);

        Assert.True(await CreatePublisher().CancelAsync(_profile.Id, CancellationToken.None));

        Assert.Equal(IssueStatus.Cancelled, left.Status);
        Assert.False(await CreatePublisher().CancelAsync(_profile.Id, CancellationToken.None)); // nothing left
    }

    [Fact]
    public async Task Stop_on_shutdown_is_no_cancel_and_the_issue_is_built_again()
    {
        using var shutdown = new CancellationTokenSource();
        _sources.Results[_heise.Id] = () =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreatePublisher().PublishAsync(_profile.Id, Slot, shutdown.Token));
        Assert.Equal(IssueStatus.Building, Assert.Single(_store.Issues).Status);

        Answer(_heise, Articles("https://heise.de/x"));
        Assert.IsType<PublishResult.Delivered>(await PublishAsync());
    }

    [Fact]
    public async Task Unknown_profile_is_reported() =>
        Assert.IsType<PublishResult.ProfileNotFound>(await CreatePublisher().PublishAsync(Guid.NewGuid(), Slot, CancellationToken.None));

    public void Dispose() => _files.Dispose();

    // Sections in this order, numbered from 1.
    private void Reorder(params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            _profile.Sections.Single(s => s.Name == names[i]).Position = i + 1;
        }
    }

    private void Answer(Source source, SourceResult result) => _sources.Results[source.Id] = () => result;

    // Newest first, one hour apart, like a feed delivers them.
    private static SourceResult.Success Articles(params string[] urls) => new(
        urls.Select((url, i) => new FetchedArticle($"id:{url}", url, new Uri(url), Slot.AddHours(-1 - i), null, null, "<p>Text</p>")).ToList(),
        ETag: null,
        LastModified: null);

    // Newest first, like Articles, with headlines to match interests and rewrite.
    private static SourceResult.Success Titled(params string[] titles) => new(
        titles.Select((title, i) => new FetchedArticle($"id:{i}", title, new Uri($"https://tagesschau.de/{i}"), Slot.AddHours(-1 - i), null, null, "<p>Text</p>")).ToList(),
        ETag: null,
        LastModified: null);

    private Task<PublishResult> PublishAsync(DateTimeOffset? slot = null) =>
        CreatePublisher().PublishAsync(_profile.Id, slot ?? Slot, CancellationToken.None);

    private IssuePublisher CreatePublisher()
    {
        var time = new FixedTimeProvider(Slot);
        return new(
            _store,
            new IssueBuilder([_sources], _seen, [new MarkingProcessor(), new FakeAiProcessor()], new IssueAiSteps(_matcher, _headlines, _overview, time), _geocoder, _weather, _onThisDay, _featured, time),
            new IssueRelease(_store, [_sources], _seen, _delivery, _files, _notifier, time),
            [_renderer],
            new FakeFeedbackLinks(),
            _files,
            _locks,
            time);
    }
}
