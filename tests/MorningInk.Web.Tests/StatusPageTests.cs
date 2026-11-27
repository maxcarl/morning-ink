using System.Net;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MorningInk.Application.Publishing;
using MorningInk.Infrastructure;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Scheduling;
using MorningInk.Web.Components.Pages;
using Quartz;

namespace MorningInk.Web.Tests;

public sealed class StatusPageTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Admin_sees_the_planned_jobs_and_the_space_each_area_takes()
    {
        using var pages = await Pages.AsAdminAsync(_app);

        var page = pages.Open<SystemStatus>("Planned");

        Pages.Shows(page, "Cleanup"); // planned at start
        Assert.Contains("Nothing is running.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"row\">Database</th>", page.Markup, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"row\">Total</th>", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_time_stands_in_its_editions_zone_and_the_zone_is_named_when_they_differ()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await _app.SignedInUserAsync("erika");
        await PlanEditionAsync(TestApp.AdminName, "Europe/Berlin");
        await PlanEditionAsync("erika", "America/New_York");

        var page = pages.Open<SystemStatus>("Planned");

        // As each edition was set, not converted into the viewer's zone; ICU may put a narrow space before "AM".
        page.WaitForAssertion(() =>
        {
            Assert.Matches(@"6:00\W+AM \(Europe/Berlin\)", page.Markup);
            Assert.Matches(@"6:00\W+AM \(America/New_York\)", page.Markup);
        });
    }

    [Fact]
    public async Task Admin_is_warned_when_the_volume_runs_low_or_the_data_directory_grows_too_large()
    {
        using var tight = new TestApp(new() { ["Storage:MinFreeMegabytes"] = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Storage:WarnAboveMegabytes"] = "0" });
        using var pages = await Pages.AsAdminAsync(tight);

        var page = pages.Open<SystemStatus>("Planned");

        Pages.Shows(page, "Little space is left on the volume.");
        Pages.Shows(page, "The data directory has grown past the size");
    }

    [Fact]
    public async Task Admin_clears_the_image_cache()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var images = _app.Services.GetRequiredService<IOptions<StorageOptions>>().Value.ImageDirectory;
        Directory.CreateDirectory(images);
        File.WriteAllBytes(Path.Combine(images, "a.jpg"), new byte[10]);
        var page = pages.Open<SystemStatus>("Planned");

        Pages.Button(page, "Clear the image cache").Click();

        Pages.Shows(page, "1 images deleted.");
        Assert.Empty(Directory.EnumerateFiles(images));
    }

    [Fact]
    public async Task Users_who_are_not_admins_see_only_their_own_jobs_and_no_storage_or_backup()
    {
        using var admin = await Pages.AsAdminAsync(_app);
        using var pages = await Pages.AsUserAsync(_app, "erika");

        var page = pages.Open<SystemStatus>("Planned");

        Assert.Contains("for your editions", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Cleanup", page.Markup, StringComparison.Ordinal); // a job of no edition of theirs
        Assert.DoesNotContain("storage-heading", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("/status/backup", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Everyone_finds_the_status_page_in_the_navigation()
    {
        await _app.SignedInAdminAsync();
        var user = await _app.SignedInUserAsync("erika");

        var page = await user.GetAsync("/status");
        var home = await user.GetStringAsync("/");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("href=\"/status\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("Nav_System", home, StringComparison.Ordinal);
    }

    public void Dispose() => _app.Dispose();

    // The user's edition, delivered daily at 6:00 in the given zone and planned like after saving it.
    private async Task PlanEditionAsync(string userName, string timeZoneId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var userId = await db.DomainUsers.Where(u => u.Name == userName).Select(u => u.Id).SingleAsync();
        var profile = await scope.ServiceProvider.GetRequiredService<ProfileEditor>().GetOrCreateAsync(userId, CancellationToken.None);
        var tracked = await db.Profiles.SingleAsync(p => p.Id == profile.Id);
        tracked.TimeZoneId = timeZoneId;
        tracked.DeliveryTime = new TimeOnly(6, 0);
        await db.SaveChangesAsync();
        var scheduler = await scope.ServiceProvider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scope.ServiceProvider.GetRequiredService<IssueScheduler>().ScheduleAsync(scheduler, new ProfileSchedule(tracked.Id, tracked.DeliveryTime, timeZoneId), CancellationToken.None, catchUp: false);
    }
}
