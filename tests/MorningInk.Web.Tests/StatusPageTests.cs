using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MorningInk.Infrastructure;
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
