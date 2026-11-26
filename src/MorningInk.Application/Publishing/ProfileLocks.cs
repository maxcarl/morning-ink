using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace MorningInk.Application.Publishing;

/// <summary>
/// Makes sure two runs for the same profile never overlap within this process, and lets a running one be cancelled.
/// Register as singleton.
/// </summary>
/// <remarks>
/// ponytail: per-process only, which suffices for the single container; the unique index on (profile, slot)
/// still guards against a second process. Use a database lock if Morning Ink ever runs in several instances.
/// </remarks>
public sealed class ProfileLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<Guid, ProfileLease> _held = new();

    /// <summary>Takes the lock if it is free; dispose the lease to release it.</summary>
    public bool TryEnter(Guid profileId, [NotNullWhen(true)] out ProfileLease? lease)
    {
        var semaphore = _locks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        if (!semaphore.Wait(0))
        {
            lease = null;
            return false;
        }

        lease = new ProfileLease(() =>
        {
            _held.TryRemove(profileId, out _);
            semaphore.Release();
        });
        _held[profileId] = lease;
        return true;
    }

    /// <summary>Asks the run holding the profile's lock to stop; false when none holds it.</summary>
    public bool Cancel(Guid profileId)
    {
        if (!_held.TryGetValue(profileId, out var lease))
        {
            return false;
        }

        lease.Cancel();
        return true;
    }
}

/// <summary>A held profile lock; <see cref="CancelRequested"/> fires when someone cancels the run.</summary>
public sealed class ProfileLease : IDisposable
{
    // Not disposed: without timers or a wait handle a token source holds nothing to free, and a late Cancel stays harmless.
    private readonly CancellationTokenSource _cancel = new();
    private readonly Action _release;
    private int _released;

    internal ProfileLease(Action release) => _release = release;

    public CancellationToken CancelRequested => _cancel.Token;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _release();
        }
    }

    internal void Cancel() => _cancel.Cancel();
}
