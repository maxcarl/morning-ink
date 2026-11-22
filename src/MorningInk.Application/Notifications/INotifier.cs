using MorningInk.Domain;

namespace MorningInk.Application.Notifications;

/// <summary>Announces issues outside the reader, e.g. on the phone. A new channel is a new case in the implementation.</summary>
public interface INotifier
{
    /// <summary>Sends the notice; null when it went out, otherwise why not. Expected problems never throw.</summary>
    Task<string?> NotifyAsync(NotificationTarget target, Notice notice, CancellationToken cancellationToken);
}

/// <param name="Secret">Token for the channel, if it needs one; never logged.</param>
public sealed record NotificationTarget(NotificationChannel Channel, string Target, string? Secret);

/// <summary>What to announce.</summary>
/// <param name="Problem">Why the delivery failed, in the user's words; null for a delivered issue.</param>
/// <param name="Language">Language of the text, e.g. "de", like the issue itself.</param>
/// <param name="IsTest">A test from the edition page rather than a real issue.</param>
public sealed record Notice(string EditionTitle, int Number, int Articles, string? Problem, string Language, bool IsTest = false);
