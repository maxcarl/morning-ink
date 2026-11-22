using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Http;

/// <summary>
/// Hosts in the home network Morning Ink may reach: those from the configuration (fixed) plus those admins added in the UI
/// (stored in the database). Singleton; the connector asks it on every connection, so lookups stay in memory.
/// </summary>
public sealed class HostAllowlist(IOptions<SafeHttpOptions> options, IServiceScopeFactory scopes, TimeProvider time)
{
    private ImmutableHashSet<string> _stored = ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Entries from the configuration; shown read-only in the UI.</summary>
    public IReadOnlyList<string> Configured => options.Value.AllowedHosts;

    /// <summary>Entries admins added in the UI.</summary>
    public IReadOnlyCollection<string> Stored => _stored;

    public bool IsAllowed(string host, int port) =>
        options.Value.IsAllowlisted(host, port) || _stored.Any(entry => SafeHttpOptions.Matches(entry, host, port));

    /// <summary>Reads the stored entries; called once at start.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().AllowedHosts.Select(h => h.Entry).ToListAsync(cancellationToken);
        _stored = entries.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Takes back an entry added in the UI; configured entries cannot be removed here.</summary>
    public async Task RemoveAsync(string entry, CancellationToken cancellationToken)
    {
        var normalized = entry.Trim().ToLowerInvariant(); // stored lower case, see AddAsync
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().AllowedHosts
            .Where(h => h.Entry == normalized)
            .ExecuteDeleteAsync(cancellationToken);
        ImmutableInterlocked.Update(ref _stored, set => set.Remove(normalized));
    }

    /// <summary>Allows an entry from now on. Only call for admins; the caller checks the role.</summary>
    public async Task AddAsync(string entry, Guid adminId, CancellationToken cancellationToken)
    {
        entry = entry.Trim().ToLowerInvariant();
        if (entry.Length == 0 || _stored.Contains(entry))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        db.AllowedHosts.Add(new AllowedHost { Entry = entry, AddedBy = adminId, AddedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        ImmutableInterlocked.Update(ref _stored, set => set.Add(entry));
    }
}
