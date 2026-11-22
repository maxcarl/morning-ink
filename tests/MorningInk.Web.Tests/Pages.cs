using System.Globalization;
using System.Security.Claims;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Web.Tests;

/// <summary>
/// Renders interactive pages with bUnit on the services of a running <see cref="TestApp"/>, signed in as one of its users.
/// Clicks and inputs go straight to the component, as they would over the page's WebSocket.
/// </summary>
public sealed class Pages : BunitContext
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private Pages(TestApp app, Guid userId, string name, bool admin)
    {
        Services.AddFallbackServiceProvider(app.Services);
        var auth = AddAuthorization();
        auth.SetAuthorized(name);
        if (admin)
        {
            auth.SetRoles(nameof(UserRole.Admin));
        }

        // After the roles: setting roles replaces the claims set before.
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
    }

    /// <summary>Runs the setup, then renders as the admin.</summary>
    public static async Task<Pages> AsAdminAsync(TestApp app)
    {
        await app.SignedInAdminAsync();
        return new Pages(app, await UserIdAsync(app, TestApp.AdminName), TestApp.AdminName, admin: true);
    }

    /// <summary>Renders as a user who is not an admin; the admin must exist already.</summary>
    public static async Task<Pages> AsUserAsync(TestApp app, string name)
    {
        await app.SignedInUserAsync(name);
        return new Pages(app, await UserIdAsync(app, name), name, admin: false);
    }

    /// <summary>Renders the page and waits until it has loaded its data.</summary>
    public IRenderedComponent<TPage> Open<TPage>(string loadedWhen)
        where TPage : IComponent
    {
        // The texts the tests look for are the English ones. Set here, in a method that is not async: a culture set inside
        // an async method does not flow back to the caller, and the page renders on the caller's context.
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        var page = Render<TPage>();
        Shows(page, loadedWhen);
        return page;
    }

    public static void Shows<TPage>(IRenderedComponent<TPage> page, string text)
        where TPage : IComponent =>
        page.WaitForAssertion(() => Assert.Contains(text, page.Markup, StringComparison.Ordinal), Patience);

    /// <summary>The button whose visible text starts with the given text, optionally the n-th of several.</summary>
    public static IElement Button<TPage>(IRenderedComponent<TPage> page, string text, int index = 0)
        where TPage : IComponent =>
        page.FindAll("button").Where(b => b.TextContent.Trim().StartsWith(text, StringComparison.Ordinal)).ElementAt(index);

    /// <summary>Types into a field and submits the form around it, like pressing Enter.</summary>
    public static void Submit<TPage>(IRenderedComponent<TPage> page, params (string Selector, string Value)[] fields)
        where TPage : IComponent
    {
        foreach (var (selector, value) in fields)
        {
            page.Find(selector).Change(value);
        }

        page.Find(fields[0].Selector).Closest("form")!.Submit(); // ! is safe: every field of these pages sits in a form
    }

    private static async Task<Guid> UserIdAsync(TestApp app, string name)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Where(u => u.Name == name).Select(u => u.Id).SingleAsync();
    }
}
