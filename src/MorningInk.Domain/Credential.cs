namespace MorningInk.Domain;

/// <summary>Login data for sources that need one; several sources can share it.</summary>
public class Credential
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required string Name { get; set; }

    public CredentialMethod Method { get; set; } = CredentialMethod.Basic;

    public string? Username { get; set; }

    /// <summary>Password or token. Stored encrypted; never log or export it.</summary>
    public required string Secret { get; set; }
}
