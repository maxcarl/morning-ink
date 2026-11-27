using System.Net;
using Bunit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Identity;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

/// <summary>Family and friends: the admin manages users, and everyone sees and edits only their own things.</summary>
public sealed class UsersPageTests : IDisposable
{
    private const string StartPassword = "a start passphrase";

    private readonly TestApp _app = new();

    [Fact]
    public async Task Admin_adds_a_user_who_can_then_sign_in()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<Users>("Add user");

        Pages.Submit(page, ("#new-name", "reader"), ("#new-password", StartPassword));

        Pages.Shows(page, "reader was added.");
        var browser = _app.Browser();
        var signIn = await TestApp.SubmitAsync(browser, "/login", new() { ["Input.UserName"] = "reader", ["Input.Password"] = StartPassword });
        Assert.Equal("/", TestApp.RedirectTarget(signIn));
    }

    [Fact]
    public async Task Too_short_start_password_is_explained()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        var page = pages.Open<Users>("Add user");

        Pages.Submit(page, ("#new-name", "reader"), ("#new-password", "short"));

        Pages.Shows(page, "at least 12 characters");
        Assert.Single(await NamesAsync());
    }

    [Fact]
    public async Task User_is_deleted_only_after_confirmation_and_the_admin_has_no_delete()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await _app.SignedInUserAsync("erika");
        var page = pages.Open<Users>("erika");

        Assert.Single(page.FindAll("button"), b => b.TextContent.Trim().StartsWith("Delete", StringComparison.Ordinal)); // none for oneself
        Pages.Button(page, "Delete").Click();
        Assert.Contains("erika", await NamesAsync()); // not before the confirmation
        Pages.Button(page, "Yes, delete").Click();

        Pages.Shows(page, "erika was deleted.");
        Assert.DoesNotContain("erika", await NamesAsync());
    }

    [Fact]
    public async Task Admin_makes_a_user_an_admin_and_back_but_not_themselves()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await _app.SignedInUserAsync("erika");
        var page = pages.Open<Users>("erika");

        Assert.Single(page.FindAll("button"), b => b.TextContent.Trim().StartsWith("Make admin", StringComparison.Ordinal)); // none for oneself
        Pages.Button(page, "Make admin").Click();
        Pages.Shows(page, "erika is now an admin.");
        Assert.Equal(UserRole.Admin, await RoleAsync("erika"));

        Pages.Button(page, "Make user").Click();
        Pages.Shows(page, "erika is now a user.");
        Assert.Equal(UserRole.User, await RoleAsync("erika"));
    }

    [Fact]
    public async Task Users_who_are_not_admins_cannot_open_the_user_list()
    {
        await _app.SignedInAdminAsync();
        var user = await _app.SignedInUserAsync("erika");

        var page = await user.GetAsync("/users");
        var home = await user.GetStringAsync("/");

        Assert.NotEqual(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain("href=\"/users\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Everyone_changes_their_own_password_with_the_current_one_and_stays_signed_in()
    {
        await _app.SignedInAdminAsync();
        var browser = await _app.SignedInUserAsync("erika");

        var wrong = await TestApp.SubmitAsync(browser, "/account", new() { ["Input.Current"] = "wrong", ["Input.New"] = "a brand new passphrase" });
        var right = await TestApp.SubmitAsync(browser, "/account", new() { ["Input.Current"] = TestApp.AdminPassword, ["Input.New"] = "a brand new passphrase" });
        var afterwards = await browser.GetAsync("/account");

        Assert.Contains("The current password is not right.", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Your password was changed.", await right.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode); // the fresh cookie carries the new security stamp
        await using var scope = _app.Services.CreateAsyncScope();
        var logins = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True(await logins.CheckPasswordAsync((await logins.FindByNameAsync("erika"))!, "a brand new passphrase")); // ! is safe: created above
    }

    [Fact]
    public async Task Another_user_sees_none_of_the_admins_sources_editions_or_issues()
    {
        await _app.SignedInAdminAsync();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var admin = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
            db.Sources.Add(new Source { UserId = admin.Id, Name = "Geheimer Feed", Url = "https://example.com/feed" });
            var profile = await scope.ServiceProvider.GetRequiredService<ProfileEditor>().GetOrCreateAsync(admin.Id, CancellationToken.None);
            profile.Title = "Adminblatt";
            db.Issues.Add(new Issue { ProfileId = profile.Id, Number = 7, ScheduledFor = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, Status = IssueStatus.Delivered });
            await db.SaveChangesAsync();
        }

        var adminProfileId = await AdminProfileIdAsync();
        using var pages = await Pages.AsUserAsync(_app, "erika");

        var sources = pages.Open<Sources>("0 feeds");
        var archive = pages.Open<Archive>("No issues yet");
        var foreignEdition = pages.Open<ProfilePage>("comes out", p => p.Add(x => x.ProfileId, adminProfileId));

        Assert.DoesNotContain("Geheimer Feed", sources.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("No. 7", archive.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Adminblatt", foreignEdition.Markup, StringComparison.Ordinal); // her own edition instead
    }

    public void Dispose() => _app.Dispose();

    private async Task<List<string>> NamesAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Select(u => u.Name).ToListAsync();
    }

    private async Task<Guid> AdminProfileIdAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var admin = await db.DomainUsers.SingleAsync(u => u.Name == TestApp.AdminName);
        return await db.Profiles.Where(p => p.UserId == admin.Id).Select(p => p.Id).SingleAsync();
    }

    private async Task<UserRole> RoleAsync(string name)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Where(u => u.Name == name).Select(u => u.Role).SingleAsync();
    }
}
