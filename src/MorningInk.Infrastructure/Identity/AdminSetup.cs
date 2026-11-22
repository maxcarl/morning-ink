using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Identity;

/// <summary>Creates the first admin on an empty instance, guarded by the <see cref="SetupCode"/>.</summary>
public sealed class AdminSetup(MorningInkDbContext db, UserManager<AppUser> logins, UserDirectory users, SetupCode code)
{
    /// <summary>True while no user exists; then every page leads to the setup.</summary>
    public async Task<bool> IsNeededAsync(CancellationToken cancellationToken) => !await db.DomainUsers.AnyAsync(cancellationToken);

    /// <summary>Creates the admin, or says why not.</summary>
    /// <param name="language">The browser's language, e.g. "de", as the user's language.</param>
    /// <returns>The new login, or the reasons as Identity error codes plus "InvalidSetupCode" and "AlreadySetUp".</returns>
    public async Task<(AppUser? Login, IReadOnlyList<string> Errors)> CreateAdminAsync(
        string name,
        string password,
        string setupCode,
        string language,
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

        var (id, errors) = await users.CreateAsync(name, password, UserRole.Admin, language, cancellationToken);
        if (id is null)
        {
            return (null, errors);
        }

        code.Invalidate();
        return (await logins.FindByIdAsync(id.Value.ToString()), []);
    }
}
