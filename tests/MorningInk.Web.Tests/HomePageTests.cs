using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class HomePageTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Failed_delivery_shows_its_error_attempts_and_missing_sources_without_the_log()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await AddIssueAsync(issue =>
        {
            issue.Status = IssueStatus.Failed;
            issue.Error = "550 Mailbox unavailable";
            issue.AddLog(DateTimeOffset.UtcNow, "SourceFailed", "heise", "heise.de could not be reached.", "Unreachable", "heise.de");
            issue.Attempts.Add(new DeliveryAttempt { AttemptedAt = DateTimeOffset.UtcNow, Succeeded = false, Error = "timeout" });
            issue.Attempts.Add(new DeliveryAttempt { AttemptedAt = DateTimeOffset.UtcNow, Succeeded = false, Error = "550 Mailbox unavailable" });
        });

        var page = pages.Open<Home>("No. 1");

        Assert.Contains("Delivery failed", page.Markup, StringComparison.Ordinal);
        Assert.Contains("550 Mailbox unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Contains("2 delivery attempts", page.Markup, StringComparison.Ordinal);
        Assert.Contains("heise: heise.de could not be reached.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_is_missing_is_listed_and_sending_waits_for_it()
    {
        using var pages = await Pages.AsAdminAsync(_app);

        var page = pages.Open<Home>("Next issue");

        Assert.Contains("No recipient yet", page.Markup, StringComparison.Ordinal);
        Assert.Contains("No sections yet", page.Markup, StringComparison.Ordinal);
        Assert.Contains("No mail server yet", page.Markup, StringComparison.Ordinal);
        Assert.Matches(@"<h2 id=""next-heading""[^>]*>\w+day, \d{1,2}:\d{2}", page.Markup); // weekday and time of the next issue
        Assert.True(Pages.Button(page, "Send now").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Send_now_builds_an_issue_that_appears_on_the_page()
    {
        using var app = new TestApp(new() { ["Smtp:Host"] = "127.0.0.1" }); // never reached: the issue below has nothing to send
        using var pages = await Pages.AsAdminAsync(app);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var editor = scope.ServiceProvider.GetRequiredService<ProfileEditor>();
            var userId = await UserIdAsync(scope);
            await editor.SaveSettingsAsync(userId, (await editor.GetOrCreateAsync(userId, CancellationToken.None)).Id, new ProfileSettings("Morning Ink", "max_abc@kindle.com", "06:00", "Europe/Berlin", null), CancellationToken.None);
            await editor.AddSectionAsync(userId, (await editor.GetOrCreateAsync(userId, CancellationToken.None)).Id, "Politik", CancellationToken.None);
        }

        var page = pages.Open<Home>("Next issue");
        var send = Pages.Button(page, "Send now");
        Assert.False(send.HasAttribute("disabled")); // bUnit would click a disabled button too
        send.Click();

        // A section without sources has nothing new, so the issue ends up empty, but it is there, with its status.
        Pages.Shows(page, "No. 1");
        Pages.Shows(page, "No new articles");
    }

    [Fact]
    public async Task Every_edition_shows_with_its_next_issue_and_its_own_send_button()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var userId = await UserIdAsync(scope);
            await scope.ServiceProvider.GetRequiredService<ProfileEditor>().CreateAsync(userId, "Abendausgabe", CancellationToken.None);
        }

        var page = pages.Open<Home>("Next issue");

        Assert.Contains("Other editions", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Abendausgabe", page.Markup, StringComparison.Ordinal);
        Assert.Equal(2, page.FindAll("button").Count(b => b.TextContent.Trim().StartsWith("Send now", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Overview_names_the_next_edition_and_says_the_time_as_people_do()
    {
        using var pages = await Pages.AsAdminAsync(_app);

        var page = pages.Open<Home>("Nächste Ausgabe", language: "de");

        Assert.Matches(@"<h2 id=""next-heading""[^>]*>\w+, \d{2}:\d{2} Uhr<", page.Markup);
        Assert.Contains("„Morning Ink“ · Nr. 1", page.Markup, StringComparison.Ordinal);
    }

    public void Dispose() => _app.Dispose();

    private static Task<Guid> UserIdAsync(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Where(u => u.Name == TestApp.AdminName).Select(u => u.Id).SingleAsync();

    private async Task AddIssueAsync(Action<Issue> shape)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var profile = await scope.ServiceProvider.GetRequiredService<ProfileEditor>().GetOrCreateAsync(await UserIdAsync(scope), CancellationToken.None);
        var issue = new Issue { ProfileId = profile.Id, Number = 1, ScheduledFor = DateTimeOffset.UtcNow.AddHours(-1), CreatedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        shape(issue);
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
    }
}
