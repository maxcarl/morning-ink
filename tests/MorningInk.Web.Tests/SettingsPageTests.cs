using System.Globalization;
using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Sources;
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
    public async Task Admin_sets_a_daily_limit_and_an_own_prompt_and_can_go_back_to_the_default()
    {
        using var ai = new MorningInk.Infrastructure.Tests.Ai.FakeChatServer();
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Settings>("No password saved");
        Assert.Contains("two or three plain sentences", page.Find("#ai-prompt").GetAttribute("value") ?? page.Find("#ai-prompt").TextContent, StringComparison.Ordinal); // starts from the default

        Pages.Submit(page, ("#ai-endpoint", ai.Endpoint), ("#ai-model", "qwen2.5:7b"), ("#ai-limit", "50"), ("#ai-prompt", "Ein Satz, bitte."));
        Pages.Shows(page, "Saved.");

        var saved = await AiSettingsAsync(app);
        Assert.Equal((50, "Ein Satz, bitte."), (saved.DailyLimit, saved.SummaryPrompt));

        Pages.Button(page, "Restore default").Click();
        Pages.Submit(page, ("#ai-model", "qwen2.5:7b"));
        Pages.Shows(page, "Saved.");
        Assert.Null((await AiSettingsAsync(app)).SummaryPrompt);
    }

    [Fact]
    public async Task Admin_sees_which_articles_went_to_which_provider()
    {
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInk.Infrastructure.Persistence.MorningInkDbContext>();
            db.AiCalls.Add(new MorningInk.Domain.AiCall
            {
                At = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
                Provider = "openrouter.ai:443",
                Model = "gpt-4o-mini",
                IsLocal = false,
                UserId = Guid.NewGuid(),
                SourceName = "Technik",
                ArticleTitle = "Neuer Raspberry Pi vorgestellt",
                ArticleUrl = "https://example.com/pi",
                Characters = 1234,
                Succeeded = true,
            });
            await db.SaveChangesAsync();
        }

        var page = pages.Open<Settings>("No password saved");

        Pages.Shows(page, "openrouter.ai:443");
        Pages.Shows(page, "Neuer Raspberry Pi vorgestellt");
        Pages.Shows(page, "1 request in the last 24 hours");
    }

    private static async Task<MorningInk.Domain.AiSettings> AiSettingsAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInk.Infrastructure.Ai.AiSettingsStore>().LoadAsync(CancellationToken.None);
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

    [Fact]
    public async Task Admin_saves_a_searxng_whose_host_is_then_allowed()
    {
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Settings>("Local news");

        Pages.Submit(page, ("#searxng-url", "not an address"));
        Pages.Shows(page, "That is not a valid http or https address.");
        Pages.Submit(page, ("#searxng-url", "http://searx.lan:8080/"));

        Pages.Shows(page, "Saved.");
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal("http://searx.lan:8080", await scope.ServiceProvider.GetRequiredService<NewsSearchSettingsStore>().SearxngUrlAsync(CancellationToken.None));
        Assert.True(app.Services.GetRequiredService<HostAllowlist>().IsAllowed("searx.lan", 8080));
    }

    [Fact]
    public async Task Searxng_from_the_environment_is_shown_read_only()
    {
        using var app = new TestApp(new() { ["NewsSearch:SearxngUrl"] = "https://searx.example.com" });
        using var pages = await Pages.AsAdminAsync(app);

        var page = pages.Open<Settings>("NewsSearch__SearxngUrl");

        Assert.Equal("https://searx.example.com", page.Find("#searxng-url").GetAttribute("value"));
        Assert.True(page.Find("#searxng-url").HasAttribute("readonly"));
    }

    [Fact]
    public async Task Search_test_shows_the_query_sent_so_an_empty_answer_can_be_tried_in_searxng_itself()
    {
        using var searxng = new MorningInk.Infrastructure.Tests.Http.LocalServer(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync("{\"results\":[],\"unresponsive_engines\":[]}"u8.ToArray());
        });
        using var app = new TestApp(new() { ["NewsSearch:SearxngUrl"] = $"http://127.0.0.1:{searxng.Port}", ["Http:AllowedHosts:0"] = $"127.0.0.1:{searxng.Port}" });
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Settings>("NewsSearch__SearxngUrl");

        Pages.Button(page, "Test search").Click();

        Pages.Shows(page, "SearXNG answers: 0 news items about London.");
        Assert.Contains($"http://127.0.0.1:{searxng.Port}/search?q=London", page.Markup, StringComparison.Ordinal);
    }

    public void Dispose() => _smtp.Dispose();
}
