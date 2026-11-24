using MorningInk.Application.Rendering;
using MorningInk.Application.Scheduling;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Publishing;

/// <summary>
/// Publishes the issue of a profile for one slot: build it (<see cref="IssueBuilder"/>), render it, bring it out
/// (<see cref="IssueRelease"/>). Each slot is published at most once, one run per profile at a time, and a failed
/// delivery sends the same file again.
/// </summary>
public sealed class IssuePublisher(
    IPublishingStore store,
    IssueBuilder builder,
    IssueRelease release,
    IEnumerable<IRenderer> renderers,
    IFeedbackLinks feedback,
    IIssueFiles files,
    ProfileLocks locks,
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
        var content = await builder.BuildAsync(loaded, draft, localTime, problems, withAi, cancellationToken);
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

            var content = await builder.BuildAsync(loaded, issue, localTime, problems, withAi: true, cancellationToken);
            if (content is null)
            {
                issue.Status = IssueStatus.Empty;

                // Too few articles already said why, and they were not "no new ones".
                if (issue.Log.LastOrDefault()?.Event != "TooFewArticles")
                {
                    issue.AddLog(time.GetUtcNow(), "NoNewArticles");
                }

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

        return await release.ReleaseAsync(issue, loaded, renderer, IssueContent.BookTitleFor(profile.Title, localTime), problems, cancellationToken);
    }

    private bool HasRenderedFile(Issue issue) =>
        issue.Status is IssueStatus.Rendered or IssueStatus.Failed
        && issue.FileName is not null
        && File.Exists(files.PathFor(issue.FileName));

    // The entries first, so each article's feedback links can name its entry; then the file.
    private async Task RenderAsync(Issue issue, IssueContent content, IRenderer renderer, CancellationToken cancellationToken)
    {
        var links = new Dictionary<IssueArticle, FeedbackLinks>(ReferenceEqualityComparer.Instance);
        foreach (var article in content.Sections.SelectMany(s => s.Articles))
        {
            var entry = Entry(article.Article, article.SourceName, article.SourceId, duplicate: false);
            issue.Entries.Add(entry);
            if (feedback.LinksFor(entry.Id) is { } pair)
            {
                links[article] = pair;
            }

            // Kept as entries too, so they count as seen once the issue is out and do not come alone the next day.
            foreach (var other in article.AlsoAt)
            {
                issue.Entries.Add(Entry(other.Article, other.SourceName, other.SourceId, duplicate: true));
            }
        }

        var fileName = $"{issue.Id}{renderer.FileExtension}";
        var path = files.PathFor(fileName);
        await using (var file = File.Create(path))
        {
            await renderer.RenderAsync(content with { Feedback = links.Count == 0 ? null : links }, file, cancellationToken);
        }

        issue.FileName = fileName;
        issue.FileSize = new FileInfo(path).Length;
        issue.Status = IssueStatus.Rendered;
        issue.AddLog(time.GetUtcNow(), "Rendered", issue.FileSize / 1024);
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
}
