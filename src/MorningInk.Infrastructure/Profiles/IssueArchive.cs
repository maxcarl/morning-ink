using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Delivery;
using MorningInk.Application.Publishing;
using MorningInk.Application.Rendering;
using MorningInk.Application.Scheduling;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>An issue in the archive, with the edition it belongs to.</summary>
/// <param name="FileAvailable">Whether its file is still there to download or send again.</param>
public sealed record ArchivedIssue(Issue Issue, Profile Profile, bool FileAvailable);

/// <param name="HasMore">Whether older issues follow beyond this page.</param>
public sealed record ArchivePage(IReadOnlyList<ArchivedIssue> Issues, bool HasMore);

/// <summary>An issue's file for download.</summary>
/// <param name="Path">Full path inside the issue directory.</param>
/// <param name="DownloadName">Named like the book on the reader, e.g. "Morning Ink 2026-11-20.epub".</param>
public sealed record IssueFile(string Path, string DownloadName, string MediaType);

/// <summary>
/// The issues a user has, as long as the cleanup keeps them: list and search, download, send again, pin.
/// Every method only reaches issues of the given user's editions.
/// </summary>
public sealed class IssueArchive(
    MorningInkDbContext db,
    IIssueFiles files,
    IEnumerable<IRenderer> renderers,
    IDelivery delivery,
    ISeenArticles seen,
    ProfileLocks locks,
    TimeProvider time)
{
    private const char LikeEscape = '\\';

    /// <param name="search">Words in an article title or source name; empty lists everything.</param>
    /// <param name="take">How many issues, newest first.</param>
    public async Task<ArchivePage> ListAsync(Guid userId, string? search, int take, CancellationToken cancellationToken)
    {
        var profiles = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId).ToDictionaryAsync(p => p.Id, cancellationToken);
        var ids = profiles.Keys.ToList();
        var query = db.Issues.AsNoTracking().Where(i => ids.Contains(i.ProfileId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            // LIKE, not Contains: in SQLite it ignores case. The user's text is escaped, so "%" or "_" match only themselves.
            var pattern = $"%{EscapeLike(search.Trim())}%";
            query = query.Where(i => i.Entries.Any(e =>
                EF.Functions.Like(e.Title, pattern, LikeEscape.ToString()) || EF.Functions.Like(e.SourceName, pattern, LikeEscape.ToString())));
        }

        var issues = await query
            .OrderByDescending(i => i.ScheduledFor)
            .Take(take + 1)
            .Include(i => i.Entries)
            .Include(i => i.Attempts)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        return new ArchivePage(
            issues.Take(take).Select(i => new ArchivedIssue(i, profiles[i.ProfileId], PathOf(i) is not null)).ToList(),
            issues.Count > take);
    }

    /// <summary>The issue's file; null when it is not the user's or the file is gone.</summary>
    public async Task<IssueFile?> GetFileAsync(Guid userId, Guid issueId, CancellationToken cancellationToken)
    {
        if (await FindAsync(userId, issueId, tracked: false, cancellationToken) is not { } found
            || PathOf(found.Issue) is not { } path
            || RendererFor(path) is not { } renderer)
        {
            return null;
        }

        return new IssueFile(path, HeaderText.FileName(BookTitle(found.Issue, found.Profile), renderer.FileExtension), renderer.MediaType);
    }

    /// <summary>Sends the issue's file to its edition's recipient once more, e.g. after the reader lost it.</summary>
    /// <returns>Null when it is not the user's or the file is gone. An issue that had failed counts as delivered afterwards.</returns>
    public async Task<DeliveryResult?> ResendAsync(Guid userId, Guid issueId, CancellationToken cancellationToken)
    {
        if (await FindAsync(userId, issueId, tracked: true, cancellationToken) is not { } found
            || PathOf(found.Issue) is not { } path
            || RendererFor(path) is not { } renderer)
        {
            return null;
        }

        var (issue, profile) = found;

        // The same lock as the scheduler's: a retry of this very issue must not run at the same time.
        if (!locks.TryEnter(profile.Id, out var release))
        {
            return new DeliveryResult.Failure("Another delivery of this edition is running. Try again in a minute.", Retryable: true, "Busy");
        }

        using (release)
        {
            var result = await delivery.DeliverAsync(
                new DeliveryRequest(profile.Recipient, BookTitle(issue, profile), path, renderer.FileExtension, renderer.MediaType),
                cancellationToken);
            var now = time.GetUtcNow();
            if (result is DeliveryResult.Failure failure)
            {
                issue.Attempts.Add(new DeliveryAttempt { AttemptedAt = now, Succeeded = false, Error = failure.Message });

                // A delivered issue stays delivered; the failed resend shows in its attempts only.
                if (issue.Status != IssueStatus.Delivered)
                {
                    issue.SetError(failure.Message, failure.Reason, failure.Arguments);
                }
            }
            else
            {
                issue.Attempts.Add(new DeliveryAttempt { AttemptedAt = now, Succeeded = true });
                if (issue.Status != IssueStatus.Delivered)
                {
                    issue.Status = IssueStatus.Delivered;
                    issue.DeliveredAt = now;
                    issue.SetError(null, null, []);
                    await seen.MarkSeenAsync(profile.UserId, issue.Entries.Select(e => new Uri(e.Url)), cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return result;
        }
    }

    /// <summary>Pinned issues survive the cleanup.</summary>
    /// <returns>False when the issue is not the user's.</returns>
    public async Task<bool> SetPinnedAsync(Guid userId, Guid issueId, bool pinned, CancellationToken cancellationToken)
    {
        if (await FindAsync(userId, issueId, tracked: true, cancellationToken) is not { } found)
        {
            return false;
        }

        found.Issue.Pinned = pinned;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string EscapeLike(string text) => text
        .Replace(LikeEscape.ToString(), $"{LikeEscape}{LikeEscape}", StringComparison.Ordinal)
        .Replace("%", $"{LikeEscape}%", StringComparison.Ordinal)
        .Replace("_", $"{LikeEscape}_", StringComparison.Ordinal);

    // The title the issue had when it was sent: edition title and its local date.
    private static string BookTitle(Issue issue, Profile profile) =>
        IssueContent.BookTitleFor(profile.Title, TimeZoneInfo.ConvertTime(issue.ScheduledFor, ProfileTimeZone.Find(profile.TimeZoneId)));

    private async Task<(Issue Issue, Profile Profile)?> FindAsync(Guid userId, Guid issueId, bool tracked, CancellationToken cancellationToken)
    {
        var issues = tracked ? db.Issues : db.Issues.AsNoTracking();
        var issue = await issues.Include(i => i.Entries).Include(i => i.Attempts).AsSplitQuery()
            .SingleOrDefaultAsync(i => i.Id == issueId && db.Profiles.Any(p => p.Id == i.ProfileId && p.UserId == userId), cancellationToken);
        if (issue is null)
        {
            return null;
        }

        var profile = await db.Profiles.AsNoTracking().SingleAsync(p => p.Id == issue.ProfileId, cancellationToken);
        return (issue, profile);
    }

    private string? PathOf(Issue issue)
    {
        if (issue.FileName is null)
        {
            return null;
        }

        var path = files.PathFor(issue.FileName);
        return File.Exists(path) ? path : null;
    }

    private IRenderer? RendererFor(string path) =>
        renderers.FirstOrDefault(r => r.FileExtension.Equals(Path.GetExtension(path), StringComparison.OrdinalIgnoreCase));
}
