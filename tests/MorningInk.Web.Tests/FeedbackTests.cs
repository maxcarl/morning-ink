using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Rendering;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Web.Tests;

/// <summary>A reader taps "more of this" on the Kindle: the browser there has no sign-in, the signed link is enough.</summary>
public sealed class FeedbackTests : IDisposable
{
    private readonly TestApp _app = new(new() { ["Web:PublicUrl"] = "https://news.example.com" });

    [Fact]
    public async Task A_tap_on_the_kindle_is_recorded_and_answered_without_a_sign_in()
    {
        await _app.SignedInAdminAsync();
        var links = await LinksAsync();

        var response = await _app.Browser().GetAsync(links.More.PathAndQuery);
        var page = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("More like “Neuer Raspberry Pi”", page, StringComparison.Ordinal);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.Equal(FeedbackKind.More, (await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Feedback.SingleAsync()).Kind);
    }

    [Fact]
    public async Task A_link_that_is_not_valid_says_so_and_records_nothing()
    {
        await _app.SignedInAdminAsync();

        var response = await _app.Browser().GetAsync("/feedback/not-a-link");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("This link no longer works", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    public void Dispose() => _app.Dispose();

    // An issue of the admin with one article, as the publisher leaves it, and the links for it.
    private async Task<FeedbackLinks> LinksAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
        var profile = await db.Profiles.FirstOrDefaultAsync(p => p.UserId == user.Id) ?? db.Profiles.Add(new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = string.Empty }).Entity;
        var issue = new Issue { ProfileId = profile.Id, Number = 1, ScheduledFor = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, Status = IssueStatus.Delivered };
        var entry = new IssueEntry { Url = "https://heise.de/pi", Title = "Neuer Raspberry Pi", SourceName = "heise" };
        issue.Entries.Add(entry);
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return scope.ServiceProvider.GetRequiredService<IFeedbackLinks>().LinksFor(entry.Id)!; // ! is safe: a public address is set
    }
}
