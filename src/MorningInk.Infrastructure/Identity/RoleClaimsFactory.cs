using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Identity;

/// <summary>Adds the role and language of the domain user to the login cookie, so pages can check "Admin".</summary>
internal sealed class RoleClaimsFactory(UserManager<AppUser> logins, IOptions<IdentityOptions> options, MorningInkDbContext db)
    : UserClaimsPrincipalFactory<AppUser>(logins, options)
{
    public const string LanguageClaim = "morning-ink:language";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var domainUser = await db.DomainUsers.SingleAsync(u => u.Id == user.Id);
        identity.AddClaim(new Claim(ClaimTypes.Role, domainUser.Role.ToString()));
        identity.AddClaim(new Claim(LanguageClaim, domainUser.Language));
        return identity;
    }
}
