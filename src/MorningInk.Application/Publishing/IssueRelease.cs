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
/// Brings a rendered issue out: by mail, or stored for the reader to fetch. Only then do its articles count as seen,
/// are their sources told (e.g. FreshRSS marks them read) and is the edition's notification sent.
/// </summary>
public sealed class IssueRelease(
    IPublishingStore store,
    IEnumerable<ISource> sources,
    ISeenArticles seen,
    IDelivery delivery,
    IIssueFiles files,
    INotifier notifier,
    TimeProvider time)
{
    /// <summary>Mails the rendered issue, or only stores it for an edition the reader fetches.</summary>
    public async Task<PublishResult> ReleaseAsync(
        Issue issue,
        PublishingProfile loaded,
        IRenderer renderer,
        string bookTitle,
        IReadOnlyList<SourceProblem> problems,
        CancellationToken cancellationToken)
    {
        var profile = loaded.Profile;
        if (!profile.SendByEmail)
        {
            return await StoreAsync(issue, loaded, problems, cancellationToken);
        }

        // ! is safe: an issue is only released once it has a rendered file.
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
        issue.AddLog(now, "Delivered", profile.Recipient);
        return await CompleteAsync(issue, loaded, now, problems, cancellationToken);
    }

    // An edition to fetch: the issue is out once it is stored, for the reader to pick up from the catalog.
    private Task<PublishResult> StoreAsync(Issue issue, PublishingProfile loaded, IReadOnlyList<SourceProblem> problems, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        issue.AddLog(now, "Stored");
        return CompleteAsync(issue, loaded, now, problems, cancellationToken);
    }

    private async Task<PublishResult> CompleteAsync(Issue issue, PublishingProfile loaded, DateTimeOffset now, IReadOnlyList<SourceProblem> problems, CancellationToken cancellationToken)
    {
        issue.Status = IssueStatus.Delivered;
        issue.DeliveredAt = now;
        issue.SetError(null, null, []);

        // Only now: had the delivery failed, the articles would have to come again.
        await seen.MarkSeenAsync(loaded.Profile.UserId, issue.Entries.Select(e => new Uri(e.Url)), cancellationToken);
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
            if (!byId.TryGetValue(group.Key, out var source) || sources.FirstOrDefault(s => s.Kind == source.Kind) is not { } implementation)
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
}
