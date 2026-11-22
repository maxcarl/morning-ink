namespace MorningInk.Domain;

/// <summary>A person using Morning Ink. Profiles, sources and credentials always belong to one user.</summary>
public class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Login name, unique across all users.</summary>
    public required string Name { get; set; }

    /// <summary>UI language as a culture name, e.g. "de" or "en".</summary>
    public string Language { get; set; } = "en";

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>
    /// Hash of the password e-readers use for the OPDS catalog; null while none was made. A password of its own, so the
    /// account's password never has to be typed into a reader.
    /// </summary>
    public string? ReaderPasswordHash { get; set; }
}
