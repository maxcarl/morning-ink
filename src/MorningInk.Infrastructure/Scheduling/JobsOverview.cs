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

/// <summary>One job on the status page.</summary>
/// <param name="At">When it started (running) or fires next (planned), in UTC.</param>
/// <param name="ProfileId">The edition an issue job is for; null for the cleanup.</param>
/// <param name="Edition">The edition's title, null for the cleanup.</param>
/// <param name="Owner">The name of the edition's user, null for the cleanup.</param>
/// <param name="TimeZoneId">The edition's time zone, the one its times were planned in; null for the cleanup.</param>
public sealed record JobRow(JobKind Kind, DateTimeOffset At, Guid? ProfileId, string? Edition, string? Owner, string? TimeZoneId);

/// <param name="Running">Jobs at work now, the longest running first.</param>
/// <param name="Planned">Triggers still to fire, the next first.</param>
/// <param name="TimeZoneId">The viewer's first edition's time zone, for jobs without an edition of their own (the cleanup).</param>
public sealed record JobsView(IReadOnlyList<JobRow> Running, IReadOnlyList<JobRow> Planned, string TimeZoneId);

/// <summary>
/// The jobs that run now or are planned, and cancelling an issue being built: an admin sees and cancels those of all
/// users, the cleanup included; everyone else only those of their own editions.
/// </summary>
/// <remarks>Quartz keeps its plan in memory, so this asks it directly rather than the database.</remarks>
public sealed class JobsOverview(ISchedulerFactory schedulers, MorningInkDbContext db, IssuePublisher publisher)
{
    private const string Utc = "UTC";

    /// <param name="viewerId">Who looks; their editions' jobs are shown, and their time zone.</param>
    /// <param name="everyone">For an admin: the jobs of all users and the cleanup too.</param>
    public async Task<JobsView> LoadAsync(Guid viewerId, bool everyone, CancellationToken cancellationToken)
    {
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        var running = (await scheduler.QueryFireInstances(new FireInstanceQuery(), cancellationToken)).Items
            .Select(f => (Kind: KindOf(f.JobKey, f.TriggerKey), At: f.FireTimeUtc, Profile: IssueScheduler.ProfileOf(f.JobKey)))
            .ToList();
        var planned = (await scheduler.QueryTriggers(new TriggerQuery { Take = PagedQuery.All }, cancellationToken)).Items
            .Where(t => t.NextFireTimeUtc is not null)
            .Select(t => (Kind: KindOf(t.JobKey, t.Key), At: t.NextFireTimeUtc!.Value, Profile: IssueScheduler.ProfileOf(t.JobKey))) // ! is safe: filtered above
            .ToList();

        var ids = running.Select(j => j.Profile).Concat(planned.Select(j => j.Profile)).OfType<Guid>().Distinct().ToList();
        var editions = await db.Profiles.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Join(db.DomainUsers, p => p.UserId, u => u.Id, (p, u) => new { p.Id, p.Title, p.UserId, Owner = u.Name, p.TimeZoneId })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Jobs without an edition of the viewer, the cleanup among them, only for an admin.
        bool Shown(Guid? profile) =>
            everyone || (profile is { } id && editions.TryGetValue(id, out var e) && e.UserId == viewerId);

        List<JobRow> Rows(IEnumerable<(JobKind Kind, DateTimeOffset At, Guid? Profile)> jobs) =>
            jobs.Where(j => Shown(j.Profile))
                .OrderBy(j => j.At)
                .Select(j => j.Profile is { } id && editions.TryGetValue(id, out var e)
                    ? new JobRow(j.Kind, j.At, id, e.Title, e.Owner, e.TimeZoneId)
                    : new JobRow(j.Kind, j.At, j.Profile, null, null, null))
                .ToList();

        var zone = await db.Profiles.AsNoTracking()
            .Where(p => p.UserId == viewerId)
            .OrderBy(p => p.Title)
            .Select(p => p.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        return new JobsView(Rows(running), Rows(planned), zone ?? Utc);
    }

    /// <summary>Cancels the issue an edition is building, see <see cref="IssuePublisher.CancelAsync"/>.</summary>
    /// <param name="everyone">For an admin: any edition; otherwise only one of the viewer's own.</param>
    /// <returns>False when there was nothing to cancel, or the edition is not the viewer's.</returns>
    public async Task<bool> CancelAsync(Guid viewerId, bool everyone, Guid profileId, CancellationToken cancellationToken) =>
        (everyone || await db.Profiles.AnyAsync(p => p.Id == profileId && p.UserId == viewerId, cancellationToken))
        && await publisher.CancelAsync(profileId, cancellationToken);

    private static JobKind KindOf(JobKey? job, TriggerKey trigger) =>
        CleanupJob.Key.Equals(job) ? JobKind.Cleanup : IssueScheduler.KindOf(trigger);
}
