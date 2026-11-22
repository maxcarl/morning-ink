using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Identity;

/// <summary>
/// The password e-readers sign in with to the OPDS catalog. Random, shown once and stored only as a hash; it opens the
/// catalog and the issue files and nothing else, so a reader never holds the account's password.
/// </summary>
public sealed class ReaderAccess(MorningInkDbContext db, IPasswordHasher<User> hasher)
{
    // Lower case and digits without look-alikes (0/o, 1/l): easy to type on an e-reader's keyboard.
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
    private const int Groups = 4;
    private const int GroupLength = 5; // 20 characters of 32 kinds: 100 bits, far beyond guessing

    /// <summary>Makes a new password, which replaces the old one, and returns it in plain text for showing once.</summary>
    public async Task<string> CreateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.DomainUsers.SingleAsync(u => u.Id == userId, cancellationToken);
        var password = string.Join('-', Enumerable.Range(0, Groups).Select(_ => RandomNumberGenerator.GetString(Alphabet, GroupLength)));
        user.ReaderPasswordHash = hasher.HashPassword(user, password);
        await db.SaveChangesAsync(cancellationToken);
        return password;
    }

    public async Task<bool> HasPasswordAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.DomainUsers.AnyAsync(u => u.Id == userId && u.ReaderPasswordHash != null, cancellationToken);

    /// <summary>Withdraws the password; readers then cannot sign in until a new one is made.</summary>
    public async Task RevokeAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.DomainUsers.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.ReaderPasswordHash, (string?)null), cancellationToken);

    /// <summary>The user these reader credentials belong to, or null.</summary>
    public async Task<Guid?> CheckAsync(string name, string password, CancellationToken cancellationToken)
    {
        var user = await db.DomainUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Name == name, cancellationToken);
        return user?.ReaderPasswordHash is { } hash && hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed
            ? user.Id
            : null;
    }
}
