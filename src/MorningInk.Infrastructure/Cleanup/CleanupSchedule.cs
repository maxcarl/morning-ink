using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MorningInk.Application.Scheduling;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using Quartz;

namespace MorningInk.Infrastructure.Cleanup;

/// <param name="At">Local time of the daily run.</param>
/// <param name="TimeZoneId">IANA zone <paramref name="At"/> is read in.</param>
public sealed record CleanupTime(TimeOnly At, string TimeZoneId);

/// <summary>
/// When the nightly cleanup runs: set on the status page, or fixed by <c>Retention__CleanupTime</c> (UTC), which then
/// wins and makes the field read-only, like the mail settings.
/// </summary>
public sealed class CleanupSchedule(MorningInkDbContext db, IOptions<RetentionOptions> retention, ISchedulerFactory schedulers, TimeProvider time)
{
    /// <summary>Whether the time comes from the environment.</summary>
    public bool IsLocked => retention.Value.CleanupTime is not null;

    public async Task<CleanupTime> GetAsync(CancellationToken cancellationToken)
    {
        if (retention.Value.CleanupTime is { } fixedTime)
        {
            return new CleanupTime(fixedTime, MaintenanceSettings.DefaultTimeZoneId);
        }

        var saved = await db.Set<MaintenanceSettings>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new MaintenanceSettings();
        return new CleanupTime(saved.CleanupTime, saved.CleanupTimeZoneId);
    }

    /// <summary>Saves the time and plans the next run with it; false when locked or the zone is unknown.</summary>
    public async Task<bool> SaveAsync(CleanupTime cleanup, CancellationToken cancellationToken)
    {
        if (IsLocked || !TimeZoneInfo.TryFindSystemTimeZoneById(cleanup.TimeZoneId, out _))
        {
            return false;
        }

        var settings = await db.SettingsRowAsync<MaintenanceSettings>(cancellationToken);
        settings.CleanupTime = cleanup.At;
        settings.CleanupTimeZoneId = cleanup.TimeZoneId;
        await db.SaveChangesAsync(cancellationToken);
        await ScheduleAsync(cancellationToken);
        return true;
    }

    /// <summary>Plans the daily run, replacing an earlier plan; at start and after every change.</summary>
    public async Task ScheduleAsync(CancellationToken cancellationToken)
    {
        var cleanup = await GetAsync(cancellationToken);
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        await CleanupJob.ScheduleAsync(scheduler, cleanup.At, ProfileTimeZone.Find(cleanup.TimeZoneId), time, cancellationToken);
    }
}
