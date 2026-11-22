using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>One source over the period: articles fetched, articles that made it into an issue, runs that failed.</summary>
public sealed record SourceStatistics(string Name, int Fetched, int InIssues, int FailedRuns);

/// <summary>A user's last <see cref="IssueStatistics.Days"/> days in numbers.</summary>
/// <param name="AverageBuildTime">From start to the rendered file; null without a finished build.</param>
public sealed record StatisticsReport(
    int Issues,
    int Delivered,
    int Failed,
    long? AverageBytes,
    double? AverageArticles,
    TimeSpan? AverageBuildTime,
    IReadOnlyList<SourceStatistics> Sources,
    int AiCalls,
    long AiCharacters);

/// <summary>
/// Statistics from what is stored anyway: the issues' log ("Fetched", "SourceFailed", "Rendered") and entries, and the
/// record of AI requests. Nothing is counted extra while publishing.
/// </summary>
public sealed class IssueStatistics(MorningInkDbContext db, TimeProvider time)
{
    public const int Days = 30;

    public async Task<StatisticsReport> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow().AddDays(-Days);
        var issues = await db.Issues.AsNoTracking()
            .Where(i => i.CreatedAt >= since && db.Profiles.Any(p => p.Id == i.ProfileId && p.UserId == userId))
            .Include(i => i.Entries)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        // Size and articles of every issue that got a file, also one whose delivery failed afterwards.
        var built = issues.Where(i => i.FileSize is not null).ToList();
        var ai = await db.AiCalls.AsNoTracking()
            .Where(c => c.UserId == userId && c.At >= since)
            .Select(c => c.Characters)
            .ToListAsync(cancellationToken);

        return new StatisticsReport(
            issues.Count,
            issues.Count(i => i.Status == IssueStatus.Delivered),
            issues.Count(i => i.Status == IssueStatus.Failed),
            built.Count == 0 ? null : (long)built.Select(i => i.FileSize).OfType<long>().Average(),
            built.Count == 0 ? null : built.Average(i => i.Entries.Count(e => !e.Duplicate)),
            AverageBuildTime(issues),
            await SourcesAsync(userId, issues, cancellationToken),
            ai.Count,
            ai.Sum(c => (long)c));
    }

    // Started, or started ahead, until the file is rendered; issues still building or failed before have no time.
    private static TimeSpan? AverageBuildTime(IEnumerable<Issue> issues)
    {
        var times = issues
            .Select(i => (Start: i.Log.FirstOrDefault(e => e.Event is "Started" or "StartedAhead")?.At, End: i.Log.FirstOrDefault(e => e.Event == "Rendered")?.At))
            .Select(t => t is { Start: { } start, End: { } end } ? (end - start).Ticks : (long?)null)
            .OfType<long>()
            .ToList();
        return times.Count == 0 ? null : TimeSpan.FromTicks((long)times.Average());
    }

    // Every current source, also one that never made it into an issue; the log names sources, entries know their id.
    private async Task<IReadOnlyList<SourceStatistics>> SourcesAsync(Guid userId, List<Issue> issues, CancellationToken cancellationToken)
    {
        var sources = await db.Sources.AsNoTracking().Where(s => s.UserId == userId).Select(s => new { s.Id, s.Name }).ToListAsync(cancellationToken);
        var log = issues.SelectMany(i => i.Log).ToList();
        var entries = issues.SelectMany(i => i.Entries).Where(e => !e.Duplicate).ToList();
        return sources
            .Select(s => new SourceStatistics(
                s.Name,
                log.Where(e => e.Event == "Fetched" && e.Arguments.FirstOrDefault() == s.Name).Sum(e => int.TryParse(e.Arguments.ElementAtOrDefault(1), CultureInfo.InvariantCulture, out var n) ? n : 0),
                entries.Count(e => e.SourceId == s.Id || (e.SourceId is null && e.SourceName == s.Name)),
                log.Count(e => e.Event == "SourceFailed" && e.Arguments.FirstOrDefault() == s.Name)))
            .OrderByDescending(s => s.InIssues)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
