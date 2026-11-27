using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Scheduling;
using Quartz;

namespace MorningInk.Infrastructure.Identity;

/// <summary>A user as the admin's list shows them.</summary>
public sealed record UserSummary(Guid Id, string Name, UserRole Role, int Editions);

/// <summary>
/// Users of the instance, for family and friends: the admin adds and removes them and resets passwords; everyone
/// changes their own. Errors are Identity's codes (e.g. "PasswordTooShort") plus "Self", "OwnRole", "NotAdmin", "LastAdmin" and "NotFound".
/// </summary>
public sealed class UserDirectory(MorningInkDbContext db, UserManager<AppUser> logins, ISchedulerFactory schedulers, IIssueFiles files)
{
    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken) =>
        await db.DomainUsers
            .OrderBy(u => u.Name)
            .Select(u => new UserSummary(u.Id, u.Name, u.Role, db.Profiles.Count(p => p.UserId == u.Id)))
            .ToListAsync(cancellationToken);

    /// <summary>Adds a user with a start password; the language follows their browser at the first sign-in.</summary>
    public async Task<(Guid? Id, IReadOnlyList<string> Errors)> CreateAsync(
        string name,
        string password,
        UserRole role,
        string language,
        CancellationToken cancellationToken)
    {
        var user = new User { Name = name.Trim(), Role = role, Language = language };
        var login = new AppUser { Id = user.Id, UserName = user.Name };

        // Asked first: the domain user's unique name would otherwise fail as a database error, not as this code.
        if (await logins.FindByNameAsync(user.Name) is not null)
        {
            return (null, [nameof(IdentityErrorDescriber.DuplicateUserName)]);
        }

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
        return (user.Id, []);
    }

    /// <summary>Sets a new password, e.g. when someone forgot theirs; their open sessions end with it.</summary>
    public async Task<IReadOnlyList<string>> ResetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        if (await logins.FindByIdAsync(userId.ToString()) is not { } login)
        {
            return ["NotFound"];
        }

        // Checked first: removing the old password and then failing on the new one would lock the user out.
        var problems = new List<string>();
        foreach (var validator in logins.PasswordValidators)
        {
            var result = await validator.ValidateAsync(logins, login, password);
            problems.AddRange(result.Errors.Select(e => e.Code));
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        await logins.RemovePasswordAsync(login);
        var added = await logins.AddPasswordAsync(login, password); // also renews the security stamp, which signs out old sessions
        return added.Errors.Select(e => e.Code).ToList();
    }

    /// <summary>Changes one's own password; the current one proves it is really them at the keyboard.</summary>
    public async Task<IReadOnlyList<string>> ChangeOwnPasswordAsync(Guid userId, string current, string password, CancellationToken cancellationToken)
    {
        if (await logins.FindByIdAsync(userId.ToString()) is not { } login)
        {
            return ["NotFound"];
        }

        var changed = await logins.ChangePasswordAsync(login, current, password);
        return changed.Errors.Select(e => e.Code).ToList();
    }

    /// <summary>Makes a user an admin or a plain user; null when done.</summary>
    /// <param name="adminId">
    /// Who changes it: nobody changes their own role, so the instance never loses its last admin. The new role comes
    /// with the user's next page load after the cookie's security stamp check (at most a minute); an open interactive
    /// tab keeps the old one until it is closed, deliberately, while admin actions ask the database themselves.
    /// </param>
    public async Task<string?> ChangeRoleAsync(Guid adminId, Guid userId, UserRole role, CancellationToken cancellationToken)
    {
        if (userId == adminId)
        {
            return "OwnRole";
        }

        if (!await IsAdminAsync(adminId, cancellationToken))
        {
            return "NotAdmin";
        }

        var changed = await db.DomainUsers.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.Role, role), cancellationToken);
        return changed == 0 ? "NotFound" : null;
    }

    /// <summary>Deletes a user with all their editions, issues, files, sources and credentials; null when done.</summary>
    /// <param name="adminId">Who deletes: nobody deletes themselves, and the last admin stays.</param>
    public async Task<string?> DeleteAsync(Guid adminId, Guid userId, CancellationToken cancellationToken)
    {
        if (userId == adminId)
        {
            return "Self";
        }

        if (!await IsAdminAsync(adminId, cancellationToken))
        {
            return "NotAdmin";
        }

        if (await db.DomainUsers.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken) is not { } user)
        {
            return "NotFound";
        }

        if (user.Role == UserRole.Admin && await db.DomainUsers.CountAsync(u => u.Role == UserRole.Admin, cancellationToken) == 1)
        {
            return "LastAdmin";
        }

        var profileIds = await db.Profiles.Where(p => p.UserId == userId).Select(p => p.Id).ToListAsync(cancellationToken);
        var fileNames = await db.Issues
            .Where(i => profileIds.Contains(i.ProfileId) && i.FileName != null)
            .Select(i => i.FileName!) // ! is safe: the query keeps only issues with a file
            .ToListAsync(cancellationToken);

        // Login, editions, issues, sources, credentials and seen articles go with the user (cascade).
        await db.DomainUsers.Where(u => u.Id == userId).ExecuteDeleteAsync(cancellationToken);
        foreach (var path in fileNames.Select(files.PathFor).Where(File.Exists))
        {
            File.Delete(path);
        }

        var scheduler = await schedulers.GetScheduler(cancellationToken);
        foreach (var profileId in profileIds)
        {
            await scheduler.DeleteJob(IssueScheduler.JobKeyFor(profileId), cancellationToken);
        }

        return null;
    }

    // Asked of the database, not of the page: an interactive tab opened as admin keeps that until it is closed.
    private Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        db.DomainUsers.AnyAsync(u => u.Id == userId && u.Role == UserRole.Admin, cancellationToken);
}
