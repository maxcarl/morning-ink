using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Identity;

/// <summary>Creates the first admin on an empty instance, guarded by the <see cref="SetupCode"/>.</summary>
public sealed class AdminSetup(MorningInkDbContext db, UserManager<AppUser> logins, SetupCode code)
{
    /// <summary>True while no user exists; then every page leads to the setup.</summary>
    public async Task<bool> IsNeededAsync(CancellationToken cancellationToken) => !await db.DomainUsers.AnyAsync(cancellationToken);

    /// <summary>Creates the admin, or says why not.</summary>
    /// <returns>The new login, or the reasons as Identity error codes plus "InvalidSetupCode" and "AlreadySetUp".</returns>
    public async Task<(AppUser? Login, IReadOnlyList<string> Errors)> CreateAdminAsync(
        string name,
        string password,
        string setupCode,
        CancellationToken cancellationToken)
    {
        if (!await IsNeededAsync(cancellationToken))
        {
            return (null, ["AlreadySetUp"]);
        }

        if (!code.Matches(setupCode))
        {
            return (null, ["InvalidSetupCode"]);
        }

        var user = new User { Name = name.Trim(), Role = UserRole.Admin };
        var login = new AppUser { Id = user.Id, UserName = user.Name };

        // Domain user and login together or not at all, e.g. when the password is too short.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        var created = await logins.CreateAsync(login, password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return (null, created.Errors.Select(e => e.Code).ToList());
        }

        await transaction.CommitAsync(cancellationToken);
        code.Invalidate();
        return (login, []);
    }
}
