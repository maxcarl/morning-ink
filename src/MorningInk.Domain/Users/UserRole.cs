namespace MorningInk.Domain;

public enum UserRole
{
    /// <summary>Manages only their own profiles, sources and credentials.</summary>
    User,

    /// <summary>Additionally manages users and global settings.</summary>
    Admin,
}
