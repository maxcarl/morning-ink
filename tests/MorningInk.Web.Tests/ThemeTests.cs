using System.Net;

namespace MorningInk.Web.Tests;

public sealed class ThemeTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Automatic_is_the_default()
    {
        var browser = await _app.SignedInAdminAsync();

        Assert.Contains("data-theme=\"auto\"", await browser.GetStringAsync("/sources"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chosen_scheme_is_kept_and_the_user_returns_to_the_page()
    {
        var browser = await _app.SignedInAdminAsync();

        var response = await TestApp.SubmitAsync(browser, "/sources", new() { ["theme"] = "dark" }, action: "/theme");
        var page = await browser.GetStringAsync("/profile");

        Assert.Equal("/sources", TestApp.RedirectTarget(response));
        Assert.Contains("data-theme=\"dark\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"true\">Dark</button>", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("purple", "//evil.example", "auto", "/")]
    [InlineData("light", "https://evil.example", "light", "/")]
    public async Task Unknown_schemes_and_foreign_return_urls_are_ignored(string theme, string returnUrl, string expectedTheme, string expectedTarget)
    {
        var browser = await _app.SignedInAdminAsync();

        var response = await TestApp.SubmitAsync(browser, "/", new() { ["theme"] = theme, ["returnUrl"] = returnUrl }, action: "/theme");

        Assert.Equal(expectedTarget, TestApp.RedirectTarget(response));
        Assert.Contains($"data-theme=\"{expectedTheme}\"", await browser.GetStringAsync("/"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switching_needs_the_antiforgery_token()
    {
        var browser = await _app.SignedInAdminAsync();

        var forged = await browser.PostAsync("/theme", new FormUrlEncodedContent([KeyValuePair.Create("theme", "dark")]));

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    public void Dispose() => _app.Dispose();
}
