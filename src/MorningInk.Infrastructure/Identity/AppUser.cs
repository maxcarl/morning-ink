using Microsoft.AspNetCore.Identity;

namespace MorningInk.Infrastructure.Identity;

/// <summary>
/// Login data of a <see cref="Domain.User"/>, managed by ASP.NET Identity (password hash, lockout, security stamp).
/// Shares its id with the domain user, which keeps name, language and role.
/// </summary>
public sealed class AppUser : IdentityUser<Guid>;
