namespace MorningInk.Web.Components;

/// <summary>
/// What a form of its own did, for the page around it to show: a notice, or errors, and for a source blocked in the
/// home network the host an admin could allow.
/// </summary>
public sealed record FormOutcome(string? Notice, IReadOnlyList<string>? Errors = null, string? BlockedHost = null)
{
    public static FormOutcome Failed(string error, string? blockedHost = null) => new(null, [error], blockedHost);
}
