namespace MorningInk.Domain;

/// <summary>How a credential is sent to a source.</summary>
public enum CredentialMethod
{
    /// <summary>HTTP Basic authentication with username and password.</summary>
    Basic,

    /// <summary>Token in the <c>Authorization: Bearer</c> header.</summary>
    BearerToken,
}
