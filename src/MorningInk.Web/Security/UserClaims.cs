using System.Security.Claims;

namespace MorningInk.Web;

/// <summary>The signed-in user's id, as the sign-in cookie carries it.</summary>
public static class UserClaims
{
    /// <exception cref="InvalidOperationException">No signed-in user: only call where the fallback policy required one.</exception>
    public static Guid Id(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("This needs a signed-in user."));
}
