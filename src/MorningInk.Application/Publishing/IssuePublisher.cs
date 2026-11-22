using MorningInk.Application.Delivery;
using MorningInk.Application.Notifications;
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
    INotifier notifier,
    TimeProvider time)
{
    // ponytail: EPUB for everyone; a format per profile comes with the PDF renderer.
    private const string Format = ".epub";

    /// <param name="profileId">Profile to publish.</param>
    /// <param name="scheduledFor">The slot, e.g. today 6:00 in the profile's time zone; one issue per slot.</param>
    /// <param name="cancellationToken">Cancels the run; a later run picks up where it stopped.</param>
    /// <param name="prepareOnly">Build and render, but do not send: the run at the delivery time sends the file (lead time).</param>
    public async Task<PublishResult> PublishAsync(Guid profileId, DateTimeOffset scheduledFor, CancellationToken cancellationToken, bool prepareOnly = false)
    {
        if (!locks.TryEnter(profileId, out var release))
        {
            return new PublishResult.AlreadyRunning();
        }

        using (release)
        {
            return await PublishLockedAsync(profileId, scheduledFor.ToUniversalTime(), prepareOnly, cancellationToken);
        }
    }

    /// <summary>
    /// Builds the profile's next issue as it would look now, without storing, sending or marking anything as seen.
    /// Sources are asked afresh (no cache validators), or an unchanged feed would show up empty.
    /// </summary>
    /// <param name="withAi">Whether to run AI steps too; off by default in the UI, since a slow model makes the preview slow.</param>
    public async Task<PreviewResult> PreviewAsync(Guid profileId, bool withAi, CancellationToken cancellationToken)
    {
        if (await store.LoadProfileAsync(profileId, cancellationToken) is not { } loaded)
        {
            return new PreviewResult.ProfileNotFound();
        }

        // The loaded objects are tracked, but nothing here saves them: the changes below stay with this preview.
        foreach (var source in loaded.Profile.Sections.SelectMany(s => s.Sources))
        {
            source.ETag = null;
            source.LastModified = null;
        }

        var profile = loaded.Profile;
        var localTime = TimeZoneInfo.ConvertTime(time.GetUtcNow(), ProfileTimeZone.Find(profile.TimeZoneId));
        var draft = new Issue
        {
            ProfileId = profileId,
            Number = await store.NextIssueNumberAsync(profileId, cancellationToken),
            ScheduledFor = time.GetUtcNow(),
            CreatedAt = time.GetUtcNow(),
        };
        var problems = new List<SourceProblem>();
        var content = await BuildAsync(loaded, draft, localTime, problems, cancellationToken, withAi);
        if (content is null)
        {
            return new PreviewResult.Empty(problems) { Log = draft.Log };
        }

        var renderer = renderers.First(r => r.FileExtension == Format);
        using var output = new MemoryStream();
        await renderer.RenderAsync(content, output, cancellationToken);
        return new PreviewResult.Built(output.ToArray(), renderer.FileExtension, renderer.MediaType, problems) { Log = draft.Log };
    }

    private async Task<PublishResult> PublishLockedAsync(Guid profileId, DateTimeOffset slot, bool prepareOnly, CancellationToken cancellationToken)
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
            issue.AddLog(time.GetUtcNow(), prepareOnly ? "StartedAhead" : "Started");
            await store.SaveChangesAsync(cancellationToken);

            var content = await BuildAsync(loaded, issue, localTime, problems, cancellationToken);
            if (content is null)
            {
                issue.Status = IssueStatus.Empty;
                issue.AddLog(time.GetUtcNow(), "NoNewArticles");
                await store.SaveChangesAsync(cancellationToken);
                return new PublishResult.NoNewArticles(issue.Id, problems);
            }

            await RenderAsync(issue, content, renderer, cancellationToken);
        }

        if (prepareOnly)
        {
            issue.AddLog(time.GetUtcNow(), "Prepared");
            await store.SaveChangesAsync(cancellationToken);
            return new PublishResult.Prepared(issue.Id);
        }

        return await DeliverAsync(issue, loaded, renderer, IssueContent.BookTitleFor(profile.Title, localTime), problems, cancellationToken);
    }

    // Best effort: a notice is a convenience and never decides whether the issue counts as delivered.
    private async Task NotifyAsync(Issue issue, PublishingProfile loaded, string? problem, CancellationToken cancellationToken)
    {
        var profile = loaded.Profile;
        if (profile.NotifyChannel == NotificationChannel.None || string.IsNullOrWhiteSpace(profile.NotifyTarget))
        {
            return;
        }

        string? error;
        try
        {
            var notice = new Notice(profile.Title, issue.Number, issue.Entries.Count(e => !e.Duplicate), problem, loaded.Language);
            error = await notifier.NotifyAsync(new NotificationTarget(profile.NotifyChannel, profile.NotifyTarget, profile.NotifySecret), notice, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            error = e.Message;
        }

        if (error is null)
        {
            issue.AddLog(time.GetUtcNow(), "Notified", profile.NotifyChannel);
        }
        else
        {
            issue.AddLog(time.GetUtcNow(), "NotifyFailed", profile.NotifyChannel, error);
        }
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
        CancellationToken cancellationToken,
        bool withAi = true)
    {
        var profile = loaded.Profile;
        var sections = profile.Sections.OrderBy(s => s.Position).ToList();

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
        return content.Count == 0
            ? null
            : new IssueContent(issue.Id, profile.Title, issue.Number, localTime, loaded.Language, content, problems.Select(p => p.SourceName).ToList(), await WeatherAsync(profile, cancellationToken));
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

        if (taken.Sum(t => t.AlsoAt.Count) is var merged and > 0)
        {
            issue.AddLog(time.GetUtcNow(), "Merged", merged);
        }

        return plan;
    }

    // Full text, cleaning, images and AI: most of the time an issue takes, so the log says how long.
    private async Task<List<IssueSection>> ProcessAsync(List<(Section Section, List<Pick> Picks)> plan, bool withAi, Issue issue, CancellationToken cancellationToken)
    {
        var started = time.GetUtcNow();
        var sections = new List<IssueSection>();
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
                sections.Add(new IssueSection(section.Name, articles));
            }
        }

        var kept = sections.Sum(s => s.Articles.Count);
        issue.AddLog(time.GetUtcNow(), "Processed", kept, (int)(time.GetUtcNow() - started).TotalSeconds);
        if (plan.Sum(p => p.Picks.Count) - kept is var dropped and > 0)
        {
            issue.AddLog(time.GetUtcNow(), "Dropped", dropped);
        }

        if (sections.Sum(s => s.Articles.Count(a => a.Article.AiSummary is not null)) is var summaries and > 0)
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
        issue.AddLog(time.GetUtcNow(), "Rendered", issue.FileSize / 1024);
        foreach (var article in content.Sections.SelectMany(s => s.Articles))
        {
            issue.Entries.Add(Entry(article.Article, article.SourceName, article.SourceId, duplicate: false));

            // Kept as entries too, so they count as seen once the issue is out and do not come alone the next day.
            foreach (var other in article.AlsoAt)
            {
                issue.Entries.Add(Entry(other.Article, other.SourceName, other.SourceId, duplicate: true));
            }
        }

        await store.SaveChangesAsync(cancellationToken);
    }

    private static IssueEntry Entry(FetchedArticle article, string sourceName, Guid? sourceId, bool duplicate) => new()
    {
        Url = article.Url.AbsoluteUri,
        Title = article.Title,
        SourceName = sourceName,
        SourceId = sourceId,
        ExternalId = article.Id,
        Duplicate = duplicate,
    };

    private async Task<PublishResult> DeliverAsync(
        Issue issue,
        PublishingProfile loaded,
        IRenderer renderer,
        string bookTitle,
        IReadOnlyList<SourceProblem> problems,
        CancellationToken cancellationToken)
    {
        var profile = loaded.Profile;

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
            issue.AddLog(now, "DeliveryFailed", [failure.Message, failure.Reason ?? string.Empty, .. failure.Arguments]);
            issue.SetError(failure.Message, failure.Reason, failure.Arguments);

            // Once, not at every retry: a server that is down for an hour would otherwise send five messages.
            if (issue.Attempts.Count(a => !a.Succeeded) == 1)
            {
                await NotifyAsync(issue, loaded, failure.Message, cancellationToken);
            }

            await store.SaveChangesAsync(cancellationToken);
            return new PublishResult.DeliveryFailed(issue.Id, failure.Message, failure.Retryable, issue.Attempts.Count);
        }

        issue.Attempts.Add(new DeliveryAttempt { AttemptedAt = now, Succeeded = true });
        issue.Status = IssueStatus.Delivered;
        issue.AddLog(now, "Delivered", profile.Recipient);
        issue.DeliveredAt = now;
        issue.SetError(null, null, []);

        // Only now: had the delivery failed, the articles would have to come again.
        await seen.MarkSeenAsync(profile.UserId, issue.Entries.Select(e => new Uri(e.Url)), cancellationToken);
        await AcknowledgeAsync(issue, loaded, cancellationToken);
        await NotifyAsync(issue, loaded, null, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return new PublishResult.Delivered(issue.Id, issue.Entries.Count(e => !e.Duplicate), problems);
    }

    // Each source hears which of its articles went out, e.g. so FreshRSS marks them read. Best effort, like the
    // notification: the issue is delivered, and a source that cannot be told only shows in the log.
    private async Task AcknowledgeAsync(Issue issue, PublishingProfile loaded, CancellationToken cancellationToken)
    {
        var byId = loaded.Profile.Sections.SelectMany(s => s.Sources).DistinctBy(s => s.Id).ToDictionary(s => s.Id);
        var delivered = issue.Entries
            .Where(e => e.SourceId is not null && e.ExternalId is not null)
            .GroupBy(e => e.SourceId!.Value); // ! is safe: filtered just above
        foreach (var group in delivered)
        {
            if (!byId.TryGetValue(group.Key, out var source) || ImplementationOf(source) is not { } implementation)
            {
                continue;
            }

            var ids = group.Select(e => e.ExternalId!).Distinct().ToList(); // ! is safe: filtered above
            try
            {
                var result = await implementation.AcknowledgeAsync(source, ids, cancellationToken);
                if (result.Problem is { } problem)
                {
                    issue.AddLog(time.GetUtcNow(), "MarkReadFailed", source.Name, problem);
                }
                else if (result.Marked > 0)
                {
                    issue.AddLog(time.GetUtcNow(), "MarkedRead", source.Name, result.Marked);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                issue.AddLog(time.GetUtcNow(), "MarkReadFailed", source.Name, e.Message);
            }
        }
    }

    /// <summary>An article chosen for a section, with the other sources that told the same story.</summary>
    private sealed record Pick(FetchedArticle Article, Source Source, List<AlsoReported> AlsoAt);
}
