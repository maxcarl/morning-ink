using MorningInk.Application.Processing;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
using MorningInk.Domain;

namespace MorningInk.Application.Publishing;

/// <summary>
/// Puts an issue's content together: fetch every source once, drop what the user has seen, choose per section,
/// then fetch full texts, clean, convert images and summarize. A failed source only costs its own articles.
/// </summary>
public sealed class IssueBuilder(
    IEnumerable<ISource> sources,
    ISeenArticles seen,
    IEnumerable<IProcessor> processors,
    IIssueOverview overview,
    IGeocoder geocoder,
    IWeatherForecast weather,
    TimeProvider time)
{
    /// <summary>The issue's content, or null when nothing new came; every step is written to the issue's log.</summary>
    /// <param name="problems">Collects the sources that failed, for the issue's note and the result.</param>
    /// <param name="withAi">Whether AI steps run; the preview may leave them out.</param>
    public async Task<IssueContent?> BuildAsync(
        PublishingProfile loaded,
        Issue issue,
        DateTimeOffset localTime,
        List<SourceProblem> problems,
        bool withAi,
        CancellationToken cancellationToken)
    {
        var profile = loaded.Profile;
        var order = profile.Sections.Where(s => s.Enabled).OrderBy(s => s.Position).ToList();
        var sections = order.Where(s => s.Kind == SectionKind.Articles).ToList();

        // Each source once, even if several sections use it.
        var bySource = new Dictionary<Guid, IReadOnlyList<FetchedArticle>>();
        foreach (var source in sections.SelectMany(s => s.Sources).DistinctBy(s => s.Id))
        {
            bySource[source.Id] = await FetchAsync(source, issue, problems, cancellationToken);
        }

        // By reference: the seen-filter hands back the very instances it keeps, one per article.
        var fresh = new HashSet<FetchedArticle>(
            await seen.FilterNewAsync(profile.UserId, bySource.Values.SelectMany(a => a).ToList(), cancellationToken),
            ReferenceEqualityComparer.Instance);
        issue.AddLog(time.GetUtcNow(), "Fresh", fresh.Count, bySource.Values.Sum(a => a.Count));

        var plan = Choose(sections, bySource, fresh, profile.Interests, issue);
        var content = await ProcessAsync(plan, withAi, issue, cancellationToken);
        if (content.Count == 0)
        {
            return null;
        }

        // An edition without a weather section has it on the title page, as before sections could be placed.
        var weather = profile.Sections.FirstOrDefault(s => s.Kind == SectionKind.Weather) is not { Enabled: false }
            ? await WeatherAsync(profile, cancellationToken)
            : null;
        var extras = await ExtrasAsync(order, content, weather is not null, loaded, issue, withAi, cancellationToken);
        return new IssueContent(issue.Id, profile.Title, issue.Number, localTime, loaded.Language, [.. content.Select(c => c.Content)], problems.Select(p => p.SourceName).ToList(), weather, extras);
    }

    // The pages that are no articles section, each placed before the first articles section that follows it.
    private async Task<List<IssueExtra>> ExtrasAsync(
        List<Section> order,
        List<(Section Section, IssueSection Content)> content,
        bool hasWeather,
        PublishingProfile loaded,
        Issue issue,
        bool withAi,
        CancellationToken cancellationToken)
    {
        var extras = new List<IssueExtra>();
        foreach (var section in order.Where(s => s.Kind != SectionKind.Articles))
        {
            var before = content.Count(c => c.Section.Position < section.Position);
            if (section.Kind == SectionKind.Weather && hasWeather)
            {
                extras.Add(new IssueExtra(IssueExtraKind.Weather, section.Name, before));
            }
            else if (section.Kind == SectionKind.Overview && withAi && await OverviewAsync(content, loaded, issue, cancellationToken) is { Count: > 0 } items)
            {
                extras.Add(new IssueExtra(IssueExtraKind.Overview, section.Name, before, items));
            }
        }

        return extras;
    }

    // A bonus like the weather: without AI, or when it fails, the issue comes without the overview.
    private async Task<IReadOnlyList<OverviewItem>?> OverviewAsync(List<(Section Section, IssueSection Content)> content, PublishingProfile loaded, Issue issue, CancellationToken cancellationToken)
    {
        var candidates = content
            .SelectMany(c => c.Content.Articles.Select(a => new OverviewCandidate(a, c.Section.Sources.FirstOrDefault(s => s.Id == a.SourceId)?.LocalAiOnly ?? false)))
            .ToList();
        IReadOnlyList<OverviewItem>? items;
        try
        {
            items = await overview.WriteAsync(new OverviewRequest(loaded.Profile.UserId, loaded.Profile.Title, loaded.Language, candidates), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            items = null;
        }

        issue.AddLog(time.GetUtcNow(), items is { Count: > 0 } ? "Overview" : "OverviewSkipped", items?.Count ?? 0);
        return items;
    }

    // All sections are chosen before any article is processed, so a story another section already took is merged
    // into that one instead of appearing twice.
    private List<(Section Section, List<Pick> Picks)> Choose(
        List<Section> sections,
        Dictionary<Guid, IReadOnlyList<FetchedArticle>> bySource,
        HashSet<FetchedArticle> fresh,
        IReadOnlyList<Interest> interests,
        Issue issue)
    {
        var taken = new List<Pick>();
        var plan = new List<(Section, List<Pick>)>();
        foreach (var section in sections)
        {
            var unread = section.Sources
                .SelectMany(source => bySource[source.Id].Select(article => (Article: article, Source: source)))
                .Where(candidate => fresh.Contains(candidate.Article))
                .ToList();
            var candidates = unread
                .Where(candidate => ArticleFilter.Admits(candidate.Article, candidate.Source) && !Interests.Hides(candidate.Article, interests))

                // The edition's interests first, then the newest: when the limit is tight, its topics win.
                .OrderByDescending(candidate => Interests.Score(candidate.Article, interests))
                .ThenByDescending(candidate => candidate.Article.PublishedAt ?? DateTimeOffset.MinValue)
                .ToList();
            if (unread.Count > candidates.Count)
            {
                issue.AddLog(time.GetUtcNow(), "Filtered", section.Name, unread.Count - candidates.Count);
            }

            var picks = new List<Pick>();
            foreach (var (article, source) in candidates)
            {
                if (picks.Count == (section.MaxArticles ?? int.MaxValue))
                {
                    break;
                }

                fresh.Remove(article); // an article appears in the first section that takes it
                if (taken.Find(t => Duplicates.AreSame(t.Article, article)) is { } same)
                {
                    // Named under the kept one, unless it is the same source telling the story twice.
                    if (same.Source.Name != source.Name)
                    {
                        same.AlsoAt.Add(new AlsoReported(source.Name, article, source.Id));
                    }

                    continue;
                }

                var pick = new Pick(article, source, []);
                taken.Add(pick);
                picks.Add(pick);
            }

            plan.Add((section, picks));
            issue.AddLog(time.GetUtcNow(), "Chosen", section.Name, picks.Count, candidates.Count);
        }

        var merged = taken.Sum(t => t.AlsoAt.Count);
        if (merged > 0)
        {
            issue.AddLog(time.GetUtcNow(), "Merged", merged);
        }

        return plan;
    }

    // Full text, cleaning, images and AI: most of the time an issue takes, so the log says how long.
    private async Task<List<(Section Section, IssueSection Content)>> ProcessAsync(List<(Section Section, List<Pick> Picks)> plan, bool withAi, Issue issue, CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        var sections = new List<(Section Section, IssueSection Content)>();
        foreach (var (section, picks) in plan)
        {
            var articles = new List<IssueArticle>();
            foreach (var (article, source, alsoAt) in picks)
            {
                // ponytail: an article too short after the full text leaves a gap in the limit; fetching spares would cost downloads.
                if (await ProcessAsync(article, source, withAi, cancellationToken) is { } processed && ArticleFilter.IsLongEnough(processed, source))
                {
                    articles.Add(new IssueArticle(processed, source.Name, alsoAt, source.Id));
                }
            }

            if (articles.Count > 0)
            {
                sections.Add((section, new IssueSection(section.Name, articles)));
            }
        }

        var kept = sections.Sum(s => s.Content.Articles.Count);
        issue.AddLog(time.GetUtcNow(), "Processed", kept, (int)(time.GetUtcNow() - started).TotalSeconds);
        var dropped = plan.Sum(p => p.Picks.Count) - kept;
        if (dropped > 0)
        {
            issue.AddLog(time.GetUtcNow(), "Dropped", dropped);
        }

        var summaries = sections.Sum(s => s.Content.Articles.Count(a => a.Article.AiSummary is not null));
        if (summaries > 0)
        {
            issue.AddLog(time.GetUtcNow(), "Summaries", summaries);
        }

        return sections;
    }

    // Weather is a bonus: whatever goes wrong here, the issue comes without it.
    private async Task<WeatherForecast?> WeatherAsync(Profile profile, CancellationToken cancellationToken)
    {
        var region = profile.Region?.Trim();
        if (string.IsNullOrEmpty(region))
        {
            return null;
        }

        try
        {
            // Geocoding only when the region changed; the result is saved with the profile.
            if (profile.Location?.Query != RegionLocation.KeyFor(region, profile.Country))
            {
                profile.Location = await geocoder.ResolveAsync(region, profile.Country, cancellationToken);
            }

            return profile.Location is null ? null : await weather.ForecastAsync(profile.Location, profile.TimeZoneId, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    // A failed source only costs its own articles. Its health and cache validators are saved with the issue.
    private async Task<IReadOnlyList<FetchedArticle>> FetchAsync(Source source, Issue issue, List<SourceProblem> problems, CancellationToken cancellationToken)
    {
        SourceResult result;
        try
        {
            result = ImplementationOf(source) is { } implementation
                ? await implementation.FetchAsync(source, cancellationToken)
                : new SourceResult.Failure($"Source type {source.Kind} is not available.", Reason: "KindUnavailable", Arguments: [source.Kind.ToString()]);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A bug in one source must not stop the newspaper; the source shows up as unavailable instead.
            result = new SourceResult.Failure(e.Message);
        }

        var now = time.GetUtcNow();
        if (result is SourceResult.Failure failure)
        {
            var problem = new SourceProblem(source.Name, failure.Message, failure.Reason, failure.Arguments);
            problems.Add(problem);
            source.RecordFailure(now, failure.Message, failure.Reason, problem.Arguments);

            // Source and English text, then code and values, so the dashboard can say it in the user's language.
            issue.AddLog(now, "SourceFailed", [problem.SourceName, problem.Message, problem.Reason ?? string.Empty, .. problem.Arguments]);
            return [];
        }

        var success = (SourceResult.Success)result;
        source.ETag = success.ETag;
        source.LastModified = success.LastModified;
        source.RecordSuccess(now, success.Articles.Count);
        issue.AddLog(now, "Fetched", source.Name, success.Articles.Count);
        return success.Articles;
    }

    private ISource? ImplementationOf(Source source) => sources.FirstOrDefault(s => s.Kind == source.Kind);

    private async Task<FetchedArticle?> ProcessAsync(FetchedArticle article, Source source, bool withAi, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var processor in processors.Where(p => withAi || !p.UsesAi))
            {
                article = await processor.ProcessAsync(article, source, cancellationToken);
            }

            return article;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Left out entirely: a half-processed article may not have been sanitized yet.
            return null;
        }
    }

    /// <summary>An article chosen for a section, with the other sources that told the same story.</summary>
    private sealed record Pick(FetchedArticle Article, Source Source, List<AlsoReported> AlsoAt);
}
