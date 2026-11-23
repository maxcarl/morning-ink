using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Ai;

/// <summary>Requests to one provider and model in the last week.</summary>
public sealed record AiProviderUsage(string Provider, string Model, bool IsLocal, int Calls, long Characters, DateTimeOffset Last);

/// <summary>What the settings page shows: per provider, the count against the limit, and the latest calls.</summary>
public sealed record AiUsageReport(IReadOnlyList<AiProviderUsage> Providers, int Last24Hours, IReadOnlyList<AiCall> Recent);

/// <summary>Records every AI request and answers how many there were, for the transparency view and the daily limit.</summary>
/// <remarks>
/// Each call uses a database context of its own: the issue run's context holds the publisher's unsaved work, which a
/// save from here must not write early.
/// </remarks>
public sealed partial class AiUsage(IServiceScopeFactory scopes, TimeProvider time, ILogger<AiUsage> logger)
{
    private const int RecentCalls = 20;
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    public DateTimeOffset Now => time.GetUtcNow();

    /// <summary>
    /// Records a request, also a failed one: the text left the instance either way. Recorded even when the run is
    /// cancelled right after the call; a record that cannot be saved is logged and never costs the result.
    /// </summary>
    public async Task RecordAsync(AiCall call)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            db.AiCalls.Add(call);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is DbUpdateException or InvalidOperationException)
        {
            LogNotRecorded(logger, e.Message);
        }
    }

    /// <summary>Whether the requests of the last 24 hours reached the limit; no limit is never reached.</summary>
    public async Task<bool> LimitReachedAsync(int? limit, CancellationToken cancellationToken) =>
        limit is { } most && await Last24HoursAsync(cancellationToken) >= most;

    /// <summary>"192.168.1.20:11434": where the text went, without path or key.</summary>
    public static string ProviderOf(string endpoint)
    {
        var uri = new Uri(endpoint);
        return $"{uri.Host}:{uri.Port}";
    }

    /// <summary>Requests in the last 24 hours, counted for the limit: a rolling window needs no time zone.</summary>
    public async Task<int> Last24HoursAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var since = Now - Day;
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().AiCalls.CountAsync(c => c.At >= since, cancellationToken);
    }

    public async Task<AiUsageReport> ReportAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var week = Now - Week;

        // Grouped in memory: a week of calls is small, and SQLite cannot aggregate the stored date format.
        var calls = await db.AiCalls.AsNoTracking().Where(c => c.At >= week).ToListAsync(cancellationToken);
        var providers = calls
            .GroupBy(c => (c.Provider, c.Model, c.IsLocal))
            .Select(g => new AiProviderUsage(g.Key.Provider, g.Key.Model, g.Key.IsLocal, g.Count(), g.Sum(c => (long)c.Characters), g.Max(c => c.At)))
            .OrderByDescending(p => p.Last)
            .ToList();
        var day = Now - Day;
        return new AiUsageReport(providers, calls.Count(c => c.At >= day), calls.OrderByDescending(c => c.At).Take(RecentCalls).ToList());
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI request could not be recorded: {Reason}")]
    private static partial void LogNotRecorded(ILogger logger, string reason);
}
