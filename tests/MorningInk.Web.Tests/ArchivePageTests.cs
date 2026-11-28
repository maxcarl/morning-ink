using System.Globalization;
using System.Net;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Tests.Delivery;
using MorningInk.Infrastructure.Tests.Rendering;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class ArchivePageTests : IDisposable
{
    private readonly FakeSmtpServer _smtp = new();
    private readonly TestApp _app;

    public ArchivePageTests() => _app = new TestApp(new()
    {
        ["Smtp:Host"] = "127.0.0.1",
        ["Smtp:Port"] = _smtp.Port.ToString(CultureInfo.InvariantCulture),
        ["Smtp:Security"] = "None",
        ["Smtp:FromAddress"] = "morning-ink@example.com",
    });

    [Fact]
    public async Task Old_issue_is_sent_to_the_kindle_again()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await AddIssueAsync("Raumfahrt: Start geglückt");
        var page = OpenFirstIssue(pages.Open<Archive>("No. 1"));

        Pages.Button(page, "Send again").Click();

        Pages.Shows(page, "Sent again to name_abc@kindle.com.");
        Assert.Single(_smtp.Messages);
    }

    [Fact]
    public async Task Search_finds_the_issue_with_an_article()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await AddIssueAsync("Raumfahrt: Start geglückt");
        var page = pages.Open<Archive>("No. 1");

        page.Find("input[type=search]").Input("wetter");
        page.WaitForAssertion(() => Assert.DoesNotContain("No. 1", page.Markup, StringComparison.Ordinal));
        page.Find("input[type=search]").Input("raumfahrt");

        Pages.Shows(page, "No. 1");
    }

    [Fact]
    public async Task Pinning_is_shown_and_saved()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var issueId = await AddIssueAsync("Raumfahrt: Start geglückt");
        var page = OpenFirstIssue(pages.Open<Archive>("No. 1"));

        Pages.Button(page, "Keep").Click();

        page.WaitForAssertion(() => Assert.Equal("true", Pages.Button(page, "Keep").GetAttribute("aria-pressed")));
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Issues.SingleAsync(i => i.Id == issueId)).Pinned);
    }

    [Fact]
    public async Task Log_shows_how_the_issue_was_built()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await AddIssueAsync("Raumfahrt: Start geglückt", issue =>
        {
            issue.AddLog(DateTimeOffset.UtcNow, "Fetched", "tagesschau", 12);
            issue.AddLog(DateTimeOffset.UtcNow, "DeliveryFailed", "The mail server smtp.example.com could not be reached.", "Unreachable", "smtp.example.com");
        });
        var page = OpenFirstIssue(pages.Open<Archive>("No. 1"));

        Pages.Button(page, "Log").Click();

        Pages.Shows(page, "tagesschau: 12 articles fetched");
        Assert.Contains("Delivery failed: The mail server smtp.example.com could not be reached.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Left_out_articles_show_why_and_lead_to_the_filter_that_decided()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var sourceId = Guid.NewGuid();
        await AddIssueAsync("Raumfahrt: Start geglückt", issue =>
        {
            issue.LeaveOut(new LeftOutArticle { Section = "Nachrichten", SourceName = "Beispiel", SourceId = sourceId, Title = "Liveticker zur Wahl", Url = "https://example.com/live", Reason = "Excluded", Detail = "Liveticker" });
            issue.LeaveOut(new LeftOutArticle { Section = "Sport", SourceName = "Beispiel", SourceId = sourceId, Title = "Tor in der Nachspielzeit", Url = "https://example.com/tor", Reason = "Interest", Detail = "Fußball" });
        });
        var page = OpenFirstIssue(pages.Open<Archive>("No. 1"));

        Pages.Button(page, "Left out (2)").Click();

        Pages.Shows(page, "Liveticker zur Wahl");
        Assert.Contains("Contains “Liveticker”", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Interest “Fußball” hidden", page.Markup, StringComparison.Ordinal);
        Assert.Contains($"/sources?edit={sourceId}#source-{sourceId}", page.Markup, StringComparison.Ordinal);
        Assert.Contains("#interests-heading", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_file_downloads_only_for_its_owner()
    {
        var admin = await _app.SignedInAdminAsync();
        var issueId = await AddIssueAsync("Raumfahrt: Start geglückt");
        var other = await _app.SignedInUserAsync("erika");

        var own = await admin.GetAsync($"/archive/{issueId}/file");
        var foreign = await other.GetAsync($"/archive/{issueId}/file");
        var anonymous = await _app.Browser().GetAsync($"/archive/{issueId}/file");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("application/epub+zip", own.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Morning Ink 2026-11-20.epub", own.Content.Headers.ContentDisposition?.FileNameStar ?? own.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.StartsWith("/login", TestApp.RedirectTarget(anonymous), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_issue_with_a_file_shows_its_cover()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var issueId = await AddIssueAsync("Raumfahrt: Start geglückt");

        var page = pages.Open<Archive>("No. 1");

        Assert.Equal($"/archive/{issueId}/cover", page.Find("img.issue-cover").GetAttribute("src"));
        Assert.Empty(page.FindAll(".cover-details")); // the actions wait until a cover is chosen
        OpenFirstIssue(page).Find("button.cover-button").Click();
        Assert.Empty(page.FindAll(".cover-details")); // and the same cover closes them again
    }

    [Fact]
    public async Task Cover_is_served_only_to_its_owner()
    {
        var admin = await _app.SignedInAdminAsync();
        var issueId = await AddIssueAsync("Raumfahrt: Start geglückt");
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            await TestEpub.WriteWithCoverAsync(scope.ServiceProvider.GetRequiredService<IIssueFiles>().PathFor($"{issueId}.epub"));
        }

        var other = await _app.SignedInUserAsync("erika");
        var own = await admin.GetAsync($"/archive/{issueId}/cover");
        var foreign = await other.GetAsync($"/archive/{issueId}/cover");
        var anonymous = await _app.Browser().GetAsync($"/archive/{issueId}/cover");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("image/jpeg", own.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.StartsWith("/login", TestApp.RedirectTarget(anonymous), StringComparison.Ordinal);
    }

    // The actions of an issue sit in its details, which its cover opens.
    private static IRenderedComponent<Archive> OpenFirstIssue(IRenderedComponent<Archive> page)
    {
        page.Find("button.cover-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".cover-details")));
        return page;
    }

    public void Dispose()
    {
        _app.Dispose();
        _smtp.Dispose();
    }

    // A delivered issue of 2026-11-20 6:00 in Berlin for the admin's edition, with its file on disk.
    private async Task<Guid> AddIssueAsync(string articleTitle, Action<Issue>? shape = null)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var userId = await db.DomainUsers.Where(u => u.Name == TestApp.AdminName).Select(u => u.Id).SingleAsync();
        var editor = scope.ServiceProvider.GetRequiredService<ProfileEditor>();
        var profile = await editor.GetOrCreateAsync(userId, CancellationToken.None);
        await editor.SaveSettingsAsync(userId, profile.Id, new ProfileSettings("Morning Ink", "name_abc@kindle.com", "06:00", "Europe/Berlin", null), CancellationToken.None);
        var slot = new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero);
        var issue = new Issue { ProfileId = profile.Id, Number = 1, ScheduledFor = slot, CreatedAt = slot, Status = IssueStatus.Delivered, DeliveredAt = slot };
        issue.FileName = $"{issue.Id}.epub";
        issue.Entries.Add(new IssueEntry { Url = "https://example.com/1", Title = articleTitle, SourceName = "Beispiel" });
        shape?.Invoke(issue);
        await File.WriteAllTextAsync(scope.ServiceProvider.GetRequiredService<IIssueFiles>().PathFor(issue.FileName), "epub");
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return issue.Id;
    }
}
