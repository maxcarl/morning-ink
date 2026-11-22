namespace MorningInk.Domain;

/// <summary>Where an edition announces a delivered or failed issue.</summary>
public enum NotificationChannel
{
    None,

    /// <summary>A topic on an ntfy server, e.g. https://ntfy.sh/my-paper.</summary>
    Ntfy,

    /// <summary>A Gotify server, with an application token.</summary>
    Gotify,

    /// <summary>A Telegram chat, through the user's own bot.</summary>
    Telegram,

    /// <summary>An e-mail through the instance's mail server.</summary>
    Email,
}
