using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Identity;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Identity;

public sealed class UserDirectoryTests : IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly TestHost _host = new();

    [Fact]
    public async Task Admin_adds_a_user_who_can_sign_in_with_the_start_password()
    {
        await using var services = await _host.StartAsync();

        var (id, errors) = await UseAsync(services, d => d.CreateAsync("reader", Password, UserRole.User, "de", CancellationToken.None));

        Assert.Empty(errors);
        Assert.True(await PasswordWorksAsync(services, "reader", Password));
        var listed = Assert.Single(await UseAsync(services, d => d.ListAsync(CancellationToken.None)));
        Assert.Equal((id, "reader", UserRole.User), (listed.Id, listed.Name, listed.Role));
    }

    [Theory]
    [InlineData("newcomer", "short", "PasswordTooShort")]
    [InlineData("reader", Password, "DuplicateUserName")]
    [InlineData("two words", Password, "InvalidUserName")]
    [InlineData("Reader", Password, "DuplicateUserName")] // names are unique ignoring case
    public async Task Invalid_users_are_refused_and_leave_nothing_behind(string name, string password, string error)
    {
        await using var services = await _host.StartAsync();
        await UseAsync(services, d => d.CreateAsync("reader", Password, UserRole.User, "en", CancellationToken.None));

        var (id, errors) = await UseAsync(services, d => d.CreateAsync(name, password, UserRole.User, "en", CancellationToken.None));

        Assert.Null(id);
        Assert.Contains(error, errors);
        Assert.Single(await UseAsync(services, d => d.ListAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task Admin_changes_another_users_role_but_never_their_own()
    {
        await using var services = await _host.StartAsync();
        var (admin, _) = await UseAsync(services, d => d.CreateAsync("admin", Password, UserRole.Admin, "en", CancellationToken.None));
        var (reader, _) = await UseAsync(services, d => d.CreateAsync("reader", Password, UserRole.User, "en", CancellationToken.None));

        Assert.Null(await UseAsync(services, d => d.ChangeRoleAsync(admin!.Value, reader!.Value, UserRole.Admin, CancellationToken.None)));
        Assert.Equal("OwnRole", await UseAsync(services, d => d.ChangeRoleAsync(admin!.Value, admin.Value, UserRole.User, CancellationToken.None))); // so an admin always stays
        Assert.Equal("NotFound", await UseAsync(services, d => d.ChangeRoleAsync(admin!.Value, Guid.NewGuid(), UserRole.User, CancellationToken.None)));

        var (other, _) = await UseAsync(services, d => d.CreateAsync("other", Password, UserRole.User, "en", CancellationToken.None));
        Assert.Equal("NotAdmin", await UseAsync(services, d => d.ChangeRoleAsync(other!.Value, admin!.Value, UserRole.User, CancellationToken.None))); // asked of the database, not the page
        Assert.Equal("NotAdmin", await UseAsync(services, d => d.DeleteAsync(other!.Value, admin!.Value, CancellationToken.None)));

        var roles = (await UseAsync(services, d => d.ListAsync(CancellationToken.None))).ToDictionary(u => u.Name, u => u.Role);
        Assert.Equal(UserRole.Admin, roles["reader"]);
        Assert.Equal(UserRole.Admin, roles["admin"]);
    }

    [Fact]
    public async Task Deleting_a_user_takes_all_their_data_and_files_and_nobody_elses()
    {
        await using var services = await _host.StartAsync();
        var (admin, _) = await UseAsync(services, d => d.CreateAsync("admin", Password, UserRole.Admin, "en", CancellationToken.None));
        var (reader, _) = await UseAsync(services, d => d.CreateAsync("reader", Password, UserRole.User, "en", CancellationToken.None));
        var readerFile = await AddIssueWithFileAsync(services, reader!.Value);
        var adminFile = await AddIssueWithFileAsync(services, admin!.Value);

        var error = await UseAsync(services, d => d.DeleteAsync(admin.Value, reader.Value, CancellationToken.None));

        Assert.Null(error);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        Assert.Equal([admin.Value], await db.DomainUsers.Select(u => u.Id).ToListAsync());
        Assert.False(await db.Profiles.AnyAsync(p => p.UserId == reader.Value));
        Assert.False(await db.Users.AnyAsync(u => u.Id == reader.Value)); // the login goes too
        Assert.False(File.Exists(readerFile));
        Assert.True(File.Exists(adminFile));
    }

    [Fact]
    public async Task Admins_cannot_delete_themselves_or_the_last_admin()
    {
        await using var services = await _host.StartAsync();
        var (admin, _) = await UseAsync(services, d => d.CreateAsync("admin", Password, UserRole.Admin, "en", CancellationToken.None));
        var (other, _) = await UseAsync(services, d => d.CreateAsync("other", Password, UserRole.Admin, "en", CancellationToken.None));

        var self = await UseAsync(services, d => d.DeleteAsync(admin!.Value, admin.Value, CancellationToken.None));
        await UseAsync(services, d => d.DeleteAsync(admin!.Value, other!.Value, CancellationToken.None));
        var unknown = await UseAsync(services, d => d.DeleteAsync(admin!.Value, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("Self", self);
        Assert.Equal("NotFound", unknown);
        Assert.Single(await UseAsync(services, d => d.ListAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task Reset_password_replaces_the_old_one_and_a_too_short_one_changes_nothing()
    {
        await using var services = await _host.StartAsync();
        var (reader, _) = await UseAsync(services, d => d.CreateAsync("reader", Password, UserRole.User, "en", CancellationToken.None));

        var tooShort = await UseAsync(services, d => d.ResetPasswordAsync(reader!.Value, "short", CancellationToken.None));
        var stillOld = await PasswordWorksAsync(services, "reader", Password);
        var errors = await UseAsync(services, d => d.ResetPasswordAsync(reader!.Value, "a brand new passphrase", CancellationToken.None));

        Assert.Contains("PasswordTooShort", tooShort);
        Assert.True(stillOld);
        Assert.Empty(errors);
        Assert.False(await PasswordWorksAsync(services, "reader", Password));
        Assert.True(await PasswordWorksAsync(services, "reader", "a brand new passphrase"));
    }

    [Fact]
    public async Task Own_password_changes_only_with_the_current_one()
    {
        await using var services = await _host.StartAsync();
        var (reader, _) = await UseAsync(services, d => d.CreateAsync("reader", Password, UserRole.User, "en", CancellationToken.None));

        var wrong = await UseAsync(services, d => d.ChangeOwnPasswordAsync(reader!.Value, "not the password", "a brand new passphrase", CancellationToken.None));
        var right = await UseAsync(services, d => d.ChangeOwnPasswordAsync(reader!.Value, Password, "a brand new passphrase", CancellationToken.None));

        Assert.Contains("PasswordMismatch", wrong);
        Assert.Empty(right);
        Assert.True(await PasswordWorksAsync(services, "reader", "a brand new passphrase"));
    }

    public void Dispose() => _host.Dispose();

    private static async Task<T> UseAsync<T>(IServiceProvider services, Func<UserDirectory, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<UserDirectory>());
    }

    private static async Task<bool> PasswordWorksAsync(IServiceProvider services, string name, string password)
    {
        await using var scope = services.CreateAsyncScope();
        var logins = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await logins.FindByNameAsync(name) is { } login && await logins.CheckPasswordAsync(login, password);
    }

    // An edition with one rendered issue on disk; returns the file's path.
    private static async Task<string> AddIssueWithFileAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        var profile = await scope.ServiceProvider.GetRequiredService<ProfileEditor>().GetOrCreateAsync(userId, CancellationToken.None);
        var issue = new Issue { ProfileId = profile.Id, Number = 1, ScheduledFor = TestHost.Now, CreatedAt = TestHost.Now, Status = IssueStatus.Delivered };
        issue.FileName = $"{issue.Id}.epub";
        var path = scope.ServiceProvider.GetRequiredService<MorningInk.Application.Publishing.IIssueFiles>().PathFor(issue.FileName);
        await File.WriteAllTextAsync(path, "epub");
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return path;
    }
}
