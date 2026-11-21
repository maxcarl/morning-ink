using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using MorningInk.Infrastructure.Identity;
using MorningInk.Web.Components;

namespace MorningInk.Web;

/// <summary>Registers and wires the web UI, so Program stays short.</summary>
public static class WebUi
{
    private const string LoginPath = "/login";
    // English is the default; German for browsers that ask for it.
    private static readonly string[] Cultures = ["en", "de"];

    // Only what the app itself serves; no inline scripts, no foreign origins, no framing by other sites.
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

    public static IServiceCollection AddWebUi(this IServiceCollection services)
    {
        services.AddRazorComponents();
        services.AddCascadingAuthenticationState();
        services.AddLocalization(options => options.ResourcesPath = "Resources");

        // Every endpoint needs a signed-in user unless it says otherwise ([AllowAnonymous]): new pages are safe by default.
        services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.ConfigureApplicationCookie(cookie =>
        {
            cookie.Cookie.Name = "morning-ink";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.LoginPath = LoginPath;
            cookie.AccessDeniedPath = LoginPath;
            cookie.ExpireTimeSpan = TimeSpan.FromDays(30);
            cookie.SlidingExpiration = true;
        });
        return services;
    }

    public static WebApplication UseWebUi(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            await next();
        });

        app.UseRequestLocalization(options => options
            .SetDefaultCulture(Cultures[0])
            .AddSupportedCultures(Cultures)
            .AddSupportedUICultures(Cultures));
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        // The app's own CSS: needed by the login page, holds no user data.
        app.MapStaticAssets().AllowAnonymous();
        app.MapRazorComponents<App>();
        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, SignInManager<AppUser> signIn) =>
        {
            // Without a valid token the request may come from another site: refuse it.
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            await signIn.SignOutAsync();
            return Results.LocalRedirect(LoginPath);
        });
        return app;
    }
}
