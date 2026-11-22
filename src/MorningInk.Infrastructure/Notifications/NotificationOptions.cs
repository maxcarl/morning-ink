namespace MorningInk.Infrastructure.Notifications;

/// <summary>Notification settings. Configured in the "Notifications" section.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Telegram's bot API; replaceable for a self-hosted Bot API server.</summary>
    public string TelegramApiUrl { get; set; } = "https://api.telegram.org";
}
