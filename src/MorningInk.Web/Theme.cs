using Microsoft.AspNetCore.Antiforgery;

namespace MorningInk.Web;

/// <summary>
/// The color scheme a user picked: automatic (follows the system), light or dark. Kept in a cookie and set without
/// JavaScript: a small form posts to /theme, the page reads the cookie into &lt;html data-theme&gt;.
/// </summary>
public static class Theme
{
    public const string Auto = "auto";
    public const string Light = "light";
    public const string Dark = "dark";

    private const string CookieName = "morning-ink-theme";

    public static IReadOnlyList<string> Choices { get; } = [Auto, Light, Dark];

    /// <summary>The user's choice; anything unknown in the cookie counts as automatic.</summary>
    public static string Of(HttpContext context) =>
        context.Request.Cookies[CookieName] is { } value && Choices.Contains(value) ? value : Auto;

    /// <summary>Handles the switcher's POST and goes back to the page it came from.</summary>
    public static void MapThemeSwitch(this WebApplication app) =>
        app.MapPost("/theme", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var choice = form["theme"].ToString();
            if (Choices.Contains(choice))
            {
                context.Response.Cookies.Append(CookieName, choice, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(365),
                    IsEssential = true,
                });
            }

            // Back to the page the switch was on, but only within this site (no open redirect).
            var returnUrl = form["returnUrl"].ToString();
            var local = returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal);
            return Results.LocalRedirect(local ? returnUrl : "/");
        });
}
