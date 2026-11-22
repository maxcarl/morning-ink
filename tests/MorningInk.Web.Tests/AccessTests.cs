using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace MorningInk.Web.Tests;

/// <summary>Nothing is reachable without signing in, except what is meant for signing in.</summary>
public sealed class AccessTests : IDisposable
{
    private readonly TestApp _app = new();

    public static TheoryData<string> ProtectedPages()
    {
        // Every routable page of the app that is not explicitly anonymous; new pages are covered automatically.
        var pages = typeof(Program).Assembly.GetTypes()
            .Where(t => !t.IsDefined(typeof(AllowAnonymousAttribute)))
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => r.Template)
            .Where(template => !template.Contains('{', StringComparison.Ordinal));
        return [.. pages];
    }

    [Theory]
    [MemberData(nameof(ProtectedPages))]
    public async Task Pages_redirect_to_login_without_a_session(string page)
    {
        await _app.SignedInAdminAsync(); // an instance that is set up, so /login does not lead to /setup

        var response = await _app.Browser().GetAsync(page);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", TestApp.RedirectTarget(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/data/morning-ink.db")]
    [InlineData("/issues/anything.epub")]
    [InlineData("/images/anything.jpg")]
    public async Task Files_from_the_data_directory_are_not_served(string path)
    {
        var response = await _app.Browser().GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Favicon_is_linked_and_served_without_sign_in()
    {
        using var app = new TestApp();
        var browser = app.Browser();

        var login = await browser.GetStringAsync("/setup"); // the first page anyone sees, before any sign-in exists
        var href = System.Text.RegularExpressions.Regex.Match(login, @"<link rel=""icon""[^>]*href=""([^""]+)""").Groups[1].Value; // Blazor writes the type's "+" as an entity
        var icon = await browser.GetAsync("/" + href);

        Assert.Equal(System.Net.HttpStatusCode.OK, icon.StatusCode);
        Assert.Equal("image/svg+xml", icon.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Health_check_needs_no_login()
    {
        var response = await _app.Browser().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Pages_send_security_headers()
    {
        var response = await _app.Browser().GetAsync("/setup");

        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Logout_needs_the_antiforgery_token()
    {
        var browser = await _app.SignedInAdminAsync();

        var forged = await browser.PostAsync("/logout", new FormUrlEncodedContent([]));
        var stillIn = await browser.GetAsync("/");

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillIn.StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var browser = await _app.SignedInAdminAsync();

        var logout = await TestApp.SubmitAsync(browser, "/", [], action: "/logout");
        var after = await browser.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    public void Dispose() => _app.Dispose();
}
