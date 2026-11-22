using System.Globalization;
using System.Net;
using Bunit;
using MorningInk.Infrastructure.Tests.Delivery;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class SettingsPageTests : IDisposable
{
    private const string Password = "correct-horse-battery-staple";

    private readonly FakeSmtpServer _smtp = new();

    [Fact]
    public async Task Admin_saves_the_mail_server_and_the_test_mail_arrives()
    {
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Settings>("No password saved");

        page.Find("#security").Change("None");
        Pages.Submit(page,
            ("#host", "127.0.0.1"),
            ("#port", _smtp.Port.ToString(CultureInfo.InvariantCulture)),
            ("#smtp-username", "max"),
            ("#smtp-password", Password),
            ("#from", "morning-ink@example.com"),
            ("#from-name", "Morning Ink"));
        Pages.Shows(page, "Saved.");
        Assert.DoesNotContain(Password, page.Markup, StringComparison.Ordinal);
        Assert.Contains("A password is saved.", page.Markup, StringComparison.Ordinal);

        Pages.Submit(page, ("#test-recipient", "max_abc@kindle.com"));

        Pages.Shows(page, "Test mail sent to max_abc@kindle.com");
        Assert.Equal("max\0" + Password, Assert.Single(_smtp.Logins));
        Assert.Single(Assert.Single(_smtp.Messages).Attachments);
    }

    [Theory]
    [InlineData("465", "SslOnConnect")]
    [InlineData("587", "StartTls")]
    public async Task Common_ports_pick_their_encryption(string port, string security)
    {
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Settings>("No password saved");

        page.Find("#port").Change(port);

        Assert.Equal(security, page.Find("#security").GetAttribute("value"));
    }

    [Fact]
    public async Task Fields_set_by_the_environment_are_read_only()
    {
        using var app = new TestApp(new() { ["Smtp:Host"] = "smtp.from-env.example" });
        using var pages = await Pages.AsAdminAsync(app);

        var page = pages.Open<Settings>("Set by the environment variable Smtp__Host.");

        Assert.True(page.Find("#host").HasAttribute("readonly"));
    }

    [Fact]
    public async Task Admin_allows_and_removes_a_home_network_host()
    {
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Settings>("No password saved");

        Pages.Submit(page, ("#new-host", "FreshRSS.lan:8080"));
        Pages.Shows(page, "freshrss.lan:8080 is allowed now.");
        Pages.Submit(page, ("#new-host", "nas.lan"));
        Pages.Shows(page, ">nas.lan<");
        Pages.Button(page, "Remove", 0).Click(); // sorted: freshrss.lan:8080 comes first

        page.WaitForAssertion(() => Assert.DoesNotContain(">freshrss.lan:8080<", page.Markup, StringComparison.Ordinal));
        Assert.Contains(">nas.lan<", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_connects_a_local_ai_and_tests_it()
    {
        using var ai = new MorningInk.Infrastructure.Tests.Ai.FakeChatServer();
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Settings>("No password saved");

        // A local provider's host is allowed in the home network when saving, so no allowlist entry is needed first.
        Pages.Submit(page, ("#ai-endpoint", ai.Endpoint), ("#ai-model", "qwen2.5:7b"));
        Pages.Shows(page, "Saved.");
        Pages.Button(page, "Test connection").Click();

        Pages.Shows(page, "The AI answers (qwen2.5:7b).");
        Assert.Single(ai.Bodies);
    }

    [Fact]
    public async Task Password_managers_leave_the_mail_and_ai_credentials_alone()
    {
        // They belong to other services; a password manager would save them as the login for Morning Ink.
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);

        var page = pages.Open<Settings>("No password saved");

        Assert.All(
            ["#smtp-username", "#smtp-password", "#from", "#ai-key", "#test-recipient"],
            selector => Assert.Equal("true", page.Find(selector).GetAttribute("data-bwignore")));
        Assert.DoesNotContain("type=\"email\"", page.Markup, StringComparison.Ordinal); // a type Bitwarden takes for a user name regardless
    }

    [Fact]
    public async Task Users_who_are_not_admins_cannot_open_the_settings()
    {
        using var app = new TestApp();
        await app.SignedInAdminAsync();
        var user = await app.SignedInUserAsync("erika");

        var page = await user.GetAsync("/settings");
        var home = await user.GetStringAsync("/");

        Assert.NotEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain("href=\"/settings\"", home, StringComparison.Ordinal);
    }

    public void Dispose() => _smtp.Dispose();
}
