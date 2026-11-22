namespace MorningInk.Web.Components.SourceForms;

/// <summary>What adding a source did, for the sources page to show: a notice, or an error with the host to allow.</summary>
public sealed record AddOutcome(string? Notice, string? Error = null, string? BlockedHost = null)
{
    public static AddOutcome Failed(string error, string? blockedHost) => new(null, error, blockedHost);
}
