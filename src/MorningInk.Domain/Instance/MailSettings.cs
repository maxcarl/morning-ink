namespace MorningInk.Domain;

/// <summary>
/// The outgoing mail server as set in the UI; there is one for the whole instance. Values set by environment
/// variables take precedence over these.
/// </summary>
public class MailSettings
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public MailSecurity Security { get; set; } = MailSecurity.StartTls;

    public string? Username { get; set; }

    /// <summary>Stored encrypted; never shown again once saved.</summary>
    public string? Password { get; set; }

    /// <summary>Sender; Amazon only accepts addresses on the account's approved list.</summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "Morning Ink";
}
