using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Updates;

namespace MorningInk.Web.Tests;

/// <summary>The version under the account page, and the notice of a newer release for admins.</summary>
public sealed class AccountPageTests : IDisposable
{
    private readonly FeedServer _github = new();

    [Fact]
    public async Task Admins_see_the_version_and_a_newer_release()
    {
        using var app = Running("1.2.0");

        var page = await (await app.SignedInAdminAsync()).GetStringAsync("/account");

        Assert.Contains("Version 1.2.0", page, StringComparison.Ordinal);
        Assert.Contains("Version 1.10.0 is available", page, StringComparison.Ordinal);
        Assert.Contains("docker compose pull", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Other_users_see_the_version_but_no_notice()
    {
        using var app = Running("1.2.0");
        await app.SignedInAdminAsync(); // the first user is the admin

        var page = await (await app.SignedInUserAsync("robin")).GetStringAsync("/account");

        Assert.Contains("Version 1.2.0", page, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"update\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_local_build_shows_dev_and_asks_nobody()
    {
        using var app = new TestApp(new() { ["Repository:TagsUrl"] = "http://192.0.2.1/tags" }); // would time out if asked

        var page = await (await app.SignedInAdminAsync()).GetStringAsync("/account");

        Assert.Contains("Version dev", page, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"update\"", page, StringComparison.Ordinal);
    }

    public void Dispose() => _github.Dispose();

    private TestApp Running(string version) => new(
        new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_github.Port}", ["Repository:TagsUrl"] = _github.TagsUrl },
        services => services.AddSingleton(new AppVersion(version)));
}
