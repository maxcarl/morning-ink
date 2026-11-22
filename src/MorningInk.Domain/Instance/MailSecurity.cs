namespace MorningInk.Domain;

/// <summary>How the connection to the mail server is encrypted.</summary>
public enum MailSecurity
{
    /// <summary>Plain connection upgraded with STARTTLS, usually port 587. Fails if the server does not offer it.</summary>
    StartTls,

    /// <summary>TLS from the first byte, usually port 465.</summary>
    SslOnConnect,

    /// <summary>Unencrypted. Only for a mail relay on the same host or in tests.</summary>
    None,
}
