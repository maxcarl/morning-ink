using System.Net;
using MorningInk.Web.Components.Pages;

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
    public async Task Users_who_are_not_admins_cannot_open_the_status_page()
    {
        await _app.SignedInAdminAsync();
        var user = await _app.SignedInUserAsync("erika");

        var page = await user.GetAsync("/status");
        var home = await user.GetStringAsync("/");

        Assert.NotEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain("href=\"/status\"", home, StringComparison.Ordinal);
    }

    public void Dispose() => _app.Dispose();
}
