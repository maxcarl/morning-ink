using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Infrastructure.Cleanup;
using MorningInk.Infrastructure.Persistence;
using Quartz;

namespace MorningInk.Infrastructure.Scheduling;

/// <summary>What a planned or running job is for.</summary>
public enum JobKind
{
    /// <summary>The issue at the delivery time.</summary>
    Daily,

    /// <summary>The issue built ahead of the delivery time (lead time).</summary>
    Build,

    /// <summary>A slot missed while Morning Ink was off.</summary>
    CatchUp,

    /// <summary>"Send now".</summary>
    Now,

    /// <summary>Another attempt at a failed delivery.</summary>
    Retry,

    /// <summary>The delivery, once the early build of its issue is done.</summary>
    AfterBuild,

    /// <summary>Deleting old issues and files.</summary>
    Cleanup,

    Other,
}

/// <summary>One job on the jobs page.</summary>
/// <param name="At">When it started (running) or fires next (planned), in UTC.</param>
/// <param name="ProfileId">The edition an issue job is for; null for the cleanup.</param>
/// <param name="Edition">The edition's title, null for the cleanup.</param>
/// <param name="Owner">The name of the edition's user, null for the cleanup.</param>
/// <param name="TimeZoneId">The edition's time zone, for showing <paramref name="At"/>; UTC for the cleanup.</param>
public sealed record JobRow(JobKind Kind, DateTimeOffset At, Guid? ProfileId, string? Edition, string? Owner, string TimeZoneId);

/// <param name="Running">Jobs at work now, the longest running first.</param>
/// <param name="Planned">Triggers still to fire, the next first.</param>
public sealed record JobsView(IReadOnlyList<JobRow> Running, IReadOnlyList<JobRow> Planned);

/// <summary>For admins: the jobs of all users that run now or are planned, and cancelling an issue being built.</summary>
/// <remarks>Quartz keeps its plan in memory, so this asks it directly rather than the database.</remarks>
public sealed class JobsOverview(ISchedulerFactory schedulers, MorningInkDbContext db, IssuePublisher publisher)
{
    private const string Utc = "UTC";

    public async Task<JobsView> LoadAsync(CancellationToken cancellationToken)
    {
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        var running = (await scheduler.QueryFireInstances(new FireInstanceQuery(), cancellationToken)).Items
            .Select(f => (Kind: KindOf(f.JobKey, f.TriggerKey), At: f.FireTimeUtc, Profile: IssueScheduler.ProfileOf(f.JobKey)))
            .OrderBy(j => j.At)
            .ToList();
        var planned = (await scheduler.QueryTriggers(new TriggerQuery { Take = PagedQuery.All }, cancellationToken)).Items
            .Where(t => t.NextFireTimeUtc is not null)
            .Select(t => (Kind: KindOf(t.JobKey, t.Key), At: t.NextFireTimeUtc!.Value, Profile: IssueScheduler.ProfileOf(t.JobKey))) // ! is safe: filtered above
            .OrderBy(j => j.At)
            .ToList();

        var ids = running.Select(j => j.Profile).Concat(planned.Select(j => j.Profile)).OfType<Guid>().Distinct().ToList();
        var editions = await db.Profiles.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Join(db.DomainUsers, p => p.UserId, u => u.Id, (p, u) => new { p.Id, p.Title, Owner = u.Name, p.TimeZoneId })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        JobRow Row(JobKind kind, DateTimeOffset at, Guid? profile) =>
            profile is { } id && editions.TryGetValue(id, out var e)
                ? new JobRow(kind, at, profile, e.Title, e.Owner, e.TimeZoneId)
                : new JobRow(kind, at, profile, null, null, Utc);

        return new JobsView(
            running.Select(j => Row(j.Kind, j.At, j.Profile)).ToList(),
            planned.Select(j => Row(j.Kind, j.At, j.Profile)).ToList());
    }

    /// <summary>Cancels the issue an edition is building, see <see cref="IssuePublisher.CancelAsync"/>.</summary>
    public Task<bool> CancelAsync(Guid profileId, CancellationToken cancellationToken) =>
        publisher.CancelAsync(profileId, cancellationToken);

    private static JobKind KindOf(JobKey? job, TriggerKey trigger) =>
        CleanupJob.Key.Equals(job) ? JobKind.Cleanup : IssueScheduler.KindOf(trigger);
}
