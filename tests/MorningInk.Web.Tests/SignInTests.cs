using System.Net;

namespace MorningInk.Web.Tests;

public sealed class SignInTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Fresh_instance_sends_everyone_to_setup()
    {
        var response = await _app.Browser().GetAsync("/login");

        Assert.Equal("/setup", TestApp.RedirectTarget(response));
    }

    [Fact]
    public async Task Setup_with_the_wrong_code_creates_nobody()
    {
        _app.SetupCode();

        var response = await TestApp.SubmitAsync(_app.Browser(), "/setup", Setup("AAAA-BBBB-CCCC-DDDD"));

        Assert.Contains("setup code is not right", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("/setup", TestApp.RedirectTarget(await _app.Browser().GetAsync("/login")));
    }

    [Fact]
    public async Task Setup_rejects_a_short_password()
    {
        var response = await TestApp.SubmitAsync(_app.Browser(), "/setup", Setup(_app.SetupCode(), password: "too short"));

        Assert.Contains("at least 12 characters", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_creates_the_admin_signs_in_and_opens_the_wizard()
    {
        var browser = _app.Browser();
        var setup = await TestApp.SubmitAsync(browser, "/setup", Setup(_app.SetupCode()));
        Assert.Equal("/welcome", TestApp.RedirectTarget(setup));

        var home = await browser.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Welcome, max.", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_is_closed_once_the_admin_exists()
    {
        await _app.SignedInAdminAsync();

        var page = await _app.Browser().GetAsync("/setup");
        var post = await TestApp.SubmitAsync(_app.Browser(), "/login", []); // the setup form is gone; a second admin cannot be made

        Assert.Equal("/login", TestApp.RedirectTarget(page));
        Assert.NotEqual(HttpStatusCode.Redirect, post.StatusCode);
    }

    [Fact]
    public async Task Login_with_the_right_password_works_and_the_wrong_one_does_not()
    {
        await _app.SignedInAdminAsync();

        var wrong = await TestApp.SubmitAsync(_app.Browser(), "/login", Login("wrong password here"));
        var right = await TestApp.SubmitAsync(_app.Browser(), "/login", Login(TestApp.AdminPassword));

        Assert.Contains("User name or password is incorrect.", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("/", TestApp.RedirectTarget(right));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        await _app.SignedInAdminAsync();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await TestApp.SubmitAsync(_app.Browser(), "/login", Login("wrong password here"));
        }

        var right = await TestApp.SubmitAsync(_app.Browser(), "/login", Login(TestApp.AdminPassword));

        Assert.Contains("Too many failed attempts", await right.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/login?ReturnUrl=%2F%2Fevil.example", "/")]
    [InlineData("/login?ReturnUrl=%2F%5Cevil.example", "/")]
    [InlineData("/login?ReturnUrl=https%3A%2F%2Fevil.example", "/")]
    [InlineData("/login?ReturnUrl=%2Fsettings", "/settings")]
    public async Task Return_url_never_leaves_the_site(string loginUrl, string expected)
    {
        await _app.SignedInAdminAsync();

        var response = await TestApp.SubmitAsync(_app.Browser(), loginUrl, Login(TestApp.AdminPassword));

        Assert.Equal(expected, TestApp.RedirectTarget(response));
    }

    [Theory]
    [InlineData(null, "Sign in")]
    [InlineData("de-DE", "Anmelden")]
    public async Task English_is_the_default_and_German_follows_the_browser(string? language, string title)
    {
        await _app.SignedInAdminAsync();

        var page = await _app.Browser(language).GetStringAsync("/login");

        Assert.Contains($"<h1>{title}</h1>", page, StringComparison.Ordinal);
    }

    public void Dispose() => _app.Dispose();

    private static Dictionary<string, string> Setup(string code, string password = TestApp.AdminPassword) => new()
    {
        ["Input.Code"] = code,
        ["Input.UserName"] = TestApp.AdminName,
        ["Input.Password"] = password,
        ["Input.ConfirmPassword"] = password,
    };

    private static Dictionary<string, string> Login(string password) => new()
    {
        ["Input.UserName"] = TestApp.AdminName,
        ["Input.Password"] = password,
    };
}
