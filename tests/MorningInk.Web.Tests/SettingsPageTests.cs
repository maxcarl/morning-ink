using System.Globalization;
using System.Net;
using MorningInk.Infrastructure.Tests.Delivery;

namespace MorningInk.Web.Tests;

public sealed class SettingsPageTests : IDisposable
{
    private const string Password = "correct-horse-battery-staple";

    private readonly FakeSmtpServer _smtp = new();

    [Fact]
    public async Task Admin_saves_the_mail_server_and_the_test_mail_arrives()
    {
        using var app = new TestApp();
        var browser = await app.SignedInAdminAsync();

        var saved = await TestApp.SubmitAsync(browser, "/settings", MailForm(), form: ("mail", null));
        var savedPage = await saved.Content.ReadAsStringAsync();
        var tested = await TestApp.SubmitAsync(browser, "/settings", new() { ["TestRecipient"] = "max_abc@kindle.com" }, form: ("test", null));

        Assert.Contains("Saved.", savedPage, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, savedPage, StringComparison.Ordinal);
        Assert.Contains("A password is saved.", savedPage, StringComparison.Ordinal);
        Assert.Contains("Test mail sent to max_abc@kindle.com", await tested.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("max\0" + Password, Assert.Single(_smtp.Logins));
        Assert.Single(Assert.Single(_smtp.Messages).Attachments);
    }

    [Fact]
    public async Task Fields_set_by_the_environment_are_read_only()
    {
        using var app = new TestApp(new() { ["Smtp:Host"] = "smtp.from-env.example" });
        var browser = await app.SignedInAdminAsync();

        var page = await browser.GetStringAsync("/settings");

        Assert.Contains("Set by the environment variable Smtp__Host.", page, StringComparison.Ordinal);
        Assert.Matches("id=\"host\" name=\"Host\"[^>]*readonly", page);
    }

    [Fact]
    public async Task Admin_allows_and_removes_a_home_network_host()
    {
        using var app = new TestApp();
        var browser = await app.SignedInAdminAsync();

        var added = await TestApp.SubmitAsync(browser, "/settings", new() { ["NewEntry"] = "FreshRSS.lan:8080" }, form: ("allow-add", null));
        var removed = await TestApp.SubmitAsync(browser, "/settings", [], form: ("allow-remove", "freshrss.lan:8080"));

        Assert.Contains("freshrss.lan:8080 is allowed now.", await added.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Nothing allowed.", await removed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
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

    private Dictionary<string, string> MailForm() => new()
    {
        ["Host"] = "127.0.0.1",
        ["Port"] = _smtp.Port.ToString(CultureInfo.InvariantCulture),
        ["Security"] = "None",
        ["Username"] = "max",
        ["Password"] = Password,
        ["FromAddress"] = "morning-ink@example.com",
        ["FromName"] = "Morning Ink",
    };
}
