using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Sources;

/// <summary>The logins of FreshRSS and newsletter sources, stored encrypted and always the user's own.</summary>
internal static class Logins
{
    /// <summary>Stores the login, or updates the password of the same login saved before, e.g. for another folder.</summary>
    public static async Task<Credential> SaveLoginAsync(this MorningInkDbContext db, Guid userId, string name, string user, string password, CancellationToken cancellationToken)
    {
        var login = await db.Credentials.SingleOrDefaultAsync(c => c.UserId == userId && c.Name == name && c.Username == user, cancellationToken);
        if (login is null)
        {
            login = new Credential { UserId = userId, Name = name, Username = user, Secret = password };
            db.Credentials.Add(login);
        }

        login.Secret = password;
        return login;
    }

    /// <summary>The source's login, checked to belong to the source's user; null when it was deleted.</summary>
    public static async Task<Credential?> LoginOfAsync(this MorningInkDbContext db, Source source, CancellationToken cancellationToken) =>
        source.CredentialId is { } id
            ? await db.Credentials.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id && c.UserId == source.UserId, cancellationToken)
            : null;
}
