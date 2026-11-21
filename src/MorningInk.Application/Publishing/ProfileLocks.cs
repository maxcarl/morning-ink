using System.Collections.Concurrent;

namespace MorningInk.Application.Publishing;

/// <summary>
/// Makes sure two runs for the same profile never overlap within this process. Register as singleton.
/// </summary>
/// <remarks>
/// ponytail: per-process only, which suffices for the single container; the unique index on (profile, slot)
/// still guards against a second process. Use a database lock if Morning Ink ever runs in several instances.
/// </remarks>
public sealed class ProfileLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    /// <summary>Takes the lock if it is free; dispose the result to release it.</summary>
    public bool TryEnter(Guid profileId, out IDisposable release)
    {
        var semaphore = _locks.GetOrAdd(profileId, _ => new SemaphoreSlim(1, 1));
        if (!semaphore.Wait(0))
        {
            release = Released.Instance;
            return false;
        }

        release = new Releaser(semaphore);
        return true;
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }

    private sealed class Released : IDisposable
    {
        public static readonly Released Instance = new();

        public void Dispose()
        {
        }
    }
}
