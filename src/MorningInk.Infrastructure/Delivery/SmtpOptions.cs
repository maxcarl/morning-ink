namespace MorningInk.Infrastructure.Delivery;

/// <summary>Outgoing mail server for Send-to-Kindle. Configured in the "Smtp" section; the password can come from an environment variable or Docker secret.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    /// <summary>Server name; empty means mail delivery is not set up yet.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    /// <summary>Encrypted by default; <see cref="SmtpSecurity.None"/> only when set explicitly.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? Username { get; set; }

    /// <summary>Never logged or shown again once saved.</summary>
    public string? Password { get; set; }

    /// <summary>Sender; Amazon only accepts addresses on the account's approved list.</summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "Morning Ink";

    /// <summary>Largest message including encoding overhead; Amazon accepts up to 50 MB.</summary>
    public long MaxMessageBytes { get; set; } = 50 * 1024 * 1024;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>How the connection to the mail server is encrypted.</summary>
public enum SmtpSecurity
{
    /// <summary>Plain connection upgraded with STARTTLS, usually port 587. Fails if the server does not offer it.</summary>
    StartTls,

    /// <summary>TLS from the first byte, usually port 465.</summary>
    SslOnConnect,

    /// <summary>Unencrypted. Only for a mail relay on the same host or in tests.</summary>
    None,
}
