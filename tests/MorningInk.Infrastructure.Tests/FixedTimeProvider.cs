namespace MorningInk.Infrastructure.Tests;

/// <summary>A clock that always returns the same instant, so time-dependent rules are deterministic.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
