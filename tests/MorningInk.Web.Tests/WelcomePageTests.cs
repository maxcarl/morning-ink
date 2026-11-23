using System.Globalization;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Tests.Delivery;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class WelcomePageTests : IDisposable
{
    private readonly FakeSmtpServer _smtp = new();
    private readonly FeedServer _feeds = new();

    [Fact]
    public async Task A_reader_that_fetches_its_issues_needs_neither_kindle_address_nor_mail_server()
    {
        using var app = new TestApp();
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Welcome>("Step 1");

        page.Find("#delivery-fetch").Change(true);
        Assert.Empty(page.FindAll("#recipient"));
        Pages.Submit(page, ("#time", Pages.BrowserTime("06:00")));

        // Straight on to the region: there is no mail to set up.
        Pages.Shows(page, "Your region");
        Pages.Button(page, "Back").Click();
        Pages.Shows(page, "Step 1");
        await using var scope = app.Services.CreateAsyncScope();
        var editor = scope.ServiceProvider.GetRequiredService<MorningInk.Infrastructure.Profiles.ProfileEditor>();
        var userId = await scope.ServiceProvider.GetRequiredService<MorningInk.Infrastructure.Persistence.MorningInkDbContext>().DomainUsers.Where(u => u.Name == TestApp.AdminName).Select(u => u.Id).SingleAsync();
        Assert.False((await editor.GetOrCreateAsync(userId, CancellationToken.None)).SendByEmail);
    }

    [Fact]
    public async Task From_an_empty_install_to_the_first_newspaper()
    {
        using var app = new TestApp(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_feeds.Port}" });
        using var pages = await Pages.AsAdminAsync(app);
        var page = pages.Open<Welcome>("Step 1");

        // 1: where the issue goes
        page.Find("#country").Change("DE");
        Pages.Submit(page, ("#recipient", "name_abc@kindle.com"), ("#time", Pages.BrowserTime("06:00")));

        // 2: mail server, then the test mail
        Pages.Shows(page, "Step 2");
        page.Find("#security").Change("None");
        Pages.Submit(page,
            ("#host", "127.0.0.1"),
            ("#port", _smtp.Port.ToString(CultureInfo.InvariantCulture)),
            ("#smtp-username", "alex"),
            ("#smtp-password", "correct-horse-battery-staple"),
            ("#from", "morning-ink@example.com"));
        Pages.Shows(page, "Test mail sent to name_abc@kindle.com");
        Assert.Contains("morning-ink@example.com", page.Markup, StringComparison.Ordinal); // to put on Amazon's approved list
        Pages.Button(page, "Next").Click();

        // 3: region, without local news (they would ask Google News)
        Pages.Shows(page, "Step 3");
        page.Find("#local").Change(false);
        Pages.Submit(page, ("#region", "52062"));

        // 4: sources. The suggestions fit the country; the test takes its own feed instead of the real ones.
        Pages.Shows(page, "Step 4");
        Assert.True(page.Find("input[value='https://www.tagesschau.de/index~rss2.xml']").HasAttribute("checked"));
        // Found again after every change: each one renders the list anew.
        for (var boxes = page.FindAll("input[type=checkbox][checked]"); boxes.Count > 0; boxes = page.FindAll("input[type=checkbox][checked]"))
        {
            boxes[0].Change(false);
        }

        await AddTestFeedAsync(app);
        Pages.Button(page, "Next").Click();

        // 5: the first issue
        Pages.Shows(page, "Step 5");
        var send = Pages.Button(page, "Send the first issue");
        Assert.False(send.HasAttribute("disabled"));
        send.Click();

        await WaitUntilAsync(() => _smtp.Messages.Count == 2); // the test mail and the newspaper
        Assert.Single(_smtp.Messages[1].Attachments);
    }

    public void Dispose()
    {
        _smtp.Dispose();
        _feeds.Dispose();
    }

    // A section with the local test feed, as if picked in step 4.
    private async Task AddTestFeedAsync(TestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
        var profile = await scope.ServiceProvider.GetRequiredService<ProfileEditor>().GetOrCreateAsync(user.Id, CancellationToken.None);
        var source = new Source { UserId = user.Id, Name = "Beispiel", Url = _feeds.FeedUrl, FetchFullText = false, MaxAge = TimeSpan.FromDays(3650) };
        db.Sources.Add(source);
        profile.Sections.Add(new Section { Name = "News", Position = 1, Sources = { source } });
        await db.SaveChangesAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(100);
        }
    }
}
