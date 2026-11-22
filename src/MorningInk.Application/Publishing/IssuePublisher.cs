using MorningInk.Application.Delivery;
using MorningInk.Application.Processing;
using MorningInk.Application.Rendering;
using MorningInk.Application.Scheduling;
using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
using MorningInk.Domain;

namespace MorningInk.Application.Publishing;

/// <summary>
/// Builds and delivers the issue of a profile for one slot: fetch every source once, drop articles the user has
/// seen, run the pipeline, render, deliver. A failed source only costs its articles; the issue appears anyway,
/// with a note. Each slot is delivered at most once, and a failed delivery resends the same file.
/// </summary>
public sealed class IssuePublisher(
    IPublishingStore store,
    IEnumerable<ISource> sources,
    ISeenArticles seen,
    IEnumerable<IProcessor> processors,
    IEnumerable<IRenderer> renderers,
    IDelivery delivery,
    IIssueFiles files,
    ProfileLocks locks,
    IGeocoder geocoder,
    IWeatherForecast weather,
    TimeProvider time)
{
    // ponytail: EPUB for everyone; a format per profile comes with the PDF renderer.
    private const string Format = ".epub";

    /// <param name="profileId">Profile to publish.</param>
    /// <param name="scheduledFor">The slot, e.g. today 6:00 in the profile's time zone; one issue per slot.</param>
    /// <param name="cancellationToken">Cancels the run; a later run picks up where it stopped.</param>
    public async Task<PublishResult> PublishAsync(Guid profileId, DateTimeOffset scheduledFor, CancellationToken cancellationToken)
    {
        if (!locks.TryEnter(profileId, out var release))
        {
            return new PublishResult.AlreadyRunning();
        }

        using (release)
        {
            return await PublishLockedAsync(profileId, scheduledFor.ToUniversalTime(), cancellationToken);
        }
    }

    private async Task<PublishResult> PublishLockedAsync(Guid profileId, DateTimeOffset slot, CancellationToken cancellationToken)
    {
        if (await store.LoadProfileAsync(profileId, cancellationToken) is not { } loaded)
        {
            return new PublishResult.ProfileNotFound();
        }

        var issue = await store.FindIssueAsync(profileId, slot, cancellationToken);
        switch (issue?.Status)
        {
            case IssueStatus.Delivered:
                return new PublishResult.AlreadyDelivered(issue.Id);
            case IssueStatus.Empty:
                return new PublishResult.NoNewArticles(issue.Id, []);
        }

        var profile = loaded.Profile;
        var renderer = renderers.First(r => r.FileExtension == Format);
        var localTime = TimeZoneInfo.ConvertTime(slot, ProfileTimeZone.Find(profile.TimeZoneId));
        if (issue is null)
        {
            issue = new Issue
            {
                ProfileId = profileId,
                Number = await store.NextIssueNumberAsync(profileId, cancellationToken),
                ScheduledFor = slot,
                CreatedAt = time.GetUtcNow(),
            };
            store.AddIssue(issue);
        }

        var problems = new List<SourceProblem>();
        if (!HasRenderedFile(issue))
        {
            // Saved before the slow part, so the unique slot is claimed and a crash leaves a visible "Building".
            issue.Status = IssueStatus.Building;
            issue.Entries.Clear();
            await store.SaveChangesAsync(cancellationToken);

            var content = await BuildAsync(loaded, issue, localTime, problems, cancellationToken);
            issue.Problems = problems.Select(p => $"{p.SourceName}: {p.Message}").ToList();
            if (content is null)
            {
                issue.Status = IssueStatus.Empty;
                await store.SaveChangesAsync(cancellationToken);
                return new PublishResult.NoNewArticles(issue.Id, problems);
            }

            await RenderAsync(issue, content, renderer, cancellationToken);
        }

        return await DeliverAsync(issue, loaded.Profile, renderer, IssueContent.BookTitleFor(profile.Title, localTime), problems, cancellationToken);
    }

    private bool HasRenderedFile(Issue issue) =>
        issue.Status is IssueStatus.Rendered or IssueStatus.Failed
        && issue.FileName is not null
        && File.Exists(files.PathFor(issue.FileName));

    private async Task<IssueContent?> BuildAsync(
        PublishingProfile loaded,
        Issue issue,
        DateTimeOffset localTime,
        List<SourceProblem> problems,
        CancellationToken cancellationToken)
    {
        var profile = loaded.Profile;
        var orderedSections = profile.Sections.OrderBy(s => s.Position).ToList();

        // Each source once, even if several sections use it.
        var bySource = new Dictionary<Guid, IReadOnlyList<FetchedArticle>>();
        foreach (var source in orderedSections.SelectMany(s => s.Sources).DistinctBy(s => s.Id))
        {
            bySource[source.Id] = await FetchAsync(source, problems, cancellationToken);
        }

        // By reference: the seen-filter hands back the very instances it keeps, one per article.
        var fresh = new HashSet<FetchedArticle>(
            await seen.FilterNewAsync(profile.UserId, bySource.Values.SelectMany(a => a).ToList(), cancellationToken),
            ReferenceEqualityComparer.Instance);

        var sections = new List<IssueSection>();
        foreach (var section in orderedSections)
        {
            var chosen = section.Sources
                .SelectMany(source => bySource[source.Id].Select(article => (Article: article, Source: source)))
                .Where(candidate => fresh.Contains(candidate.Article) && ArticleFilter.Admits(candidate.Article, candidate.Source))
                .OrderByDescending(candidate => candidate.Article.PublishedAt ?? DateTimeOffset.MinValue)
                .Take(section.MaxArticles ?? int.MaxValue)
                .ToList();

            var articles = new List<IssueArticle>();
            foreach (var (article, source) in chosen)
            {
                fresh.Remove(article); // an article appears in the first section that takes it
                // ponytail: an article too short after the full text leaves a gap in the limit; fetching spares would cost downloads.
                if (await ProcessAsync(article, source, cancellationToken) is { } processed && ArticleFilter.IsLongEnough(processed, source))
                {
                    articles.Add(new IssueArticle(processed, source.Name));
                }
            }

            if (articles.Count > 0)
            {
                sections.Add(new IssueSection(section.Name, articles));
            }
        }

        return sections.Count == 0
            ? null
            : new IssueContent(issue.Id, profile.Title, issue.Number, localTime, loaded.Language, sections, problems.Select(p => p.SourceName).ToList(), await WeatherAsync(profile, cancellationToken));
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

    private async Task<IReadOnlyList<FetchedArticle>> FetchAsync(Source source, List<SourceProblem> problems, CancellationToken cancellationToken)
    {
        if (sources.FirstOrDefault(s => s.Kind == source.Kind) is not { } implementation)
        {
            problems.Add(new SourceProblem(source.Name, $"Source type {source.Kind} is not available."));
            return [];
        }

        try
        {
            switch (await implementation.FetchAsync(source, cancellationToken))
            {
                case SourceResult.Success success:
                    // Kept for the next conditional request; saved together with the issue.
                    source.ETag = success.ETag;
                    source.LastModified = success.LastModified;
                    return success.Articles;
                case SourceResult.Failure failure:
                    problems.Add(new SourceProblem(source.Name, failure.Message));
                    return [];
                default:
                    return [];
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A bug in one source must not stop the newspaper; the source shows up as unavailable instead.
            problems.Add(new SourceProblem(source.Name, e.Message));
            return [];
        }
    }

    private async Task<FetchedArticle?> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var processor in processors)
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

    private async Task RenderAsync(Issue issue, IssueContent content, IRenderer renderer, CancellationToken cancellationToken)
    {
        var fileName = $"{issue.Id}{renderer.FileExtension}";
        var path = files.PathFor(fileName);
        await using (var file = File.Create(path))
        {
            await renderer.RenderAsync(content, file, cancellationToken);
        }

        issue.FileName = fileName;
        issue.FileSize = new FileInfo(path).Length;
        issue.Status = IssueStatus.Rendered;
        foreach (var article in content.Sections.SelectMany(s => s.Articles))
        {
            issue.Entries.Add(new IssueEntry { Url = article.Article.Url.AbsoluteUri, Title = article.Article.Title, SourceName = article.SourceName });
        }

        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task<PublishResult> DeliverAsync(
        Issue issue,
        Profile profile,
        IRenderer renderer,
        string bookTitle,
        IReadOnlyList<SourceProblem> problems,
        CancellationToken cancellationToken)
    {
        // ! is safe: DeliverAsync only runs after RenderAsync or for an issue with a rendered file.
        var path = files.PathFor(issue.FileName!);
        var result = await delivery.DeliverAsync(
            new DeliveryRequest(profile.Recipient, bookTitle, path, renderer.FileExtension, renderer.MediaType),
            cancellationToken);
        var now = time.GetUtcNow();

        if (result is DeliveryResult.Failure failure)
        {
            issue.Attempts.Add(new DeliveryAttempt { AttemptedAt = now, Succeeded = false, Error = failure.Message });
            issue.Status = IssueStatus.Failed;
            issue.Error = failure.Message;
            await store.SaveChangesAsync(cancellationToken);
            return new PublishResult.DeliveryFailed(issue.Id, failure.Message, failure.Retryable, issue.Attempts.Count);
        }

        issue.Attempts.Add(new DeliveryAttempt { AttemptedAt = now, Succeeded = true });
        issue.Status = IssueStatus.Delivered;
        issue.DeliveredAt = now;
        issue.Error = null;

        // Only now: had the delivery failed, the articles would have to come again.
        await seen.MarkSeenAsync(profile.UserId, issue.Entries.Select(e => new Uri(e.Url)), cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return new PublishResult.Delivered(issue.Id, issue.Entries.Count, problems);
    }
}
