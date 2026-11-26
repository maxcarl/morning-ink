using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Web.Tests;

/// <summary>Backup and restore on the status page, through real requests: tokens, roles and the upload.</summary>
public sealed partial class BackupTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Admin_downloads_a_backup_and_uploads_it_to_restore_at_the_next_start()
    {
        var admin = await _app.SignedInAdminAsync();

        var download = await TestApp.SubmitAsync(admin, "/status", [], action: "/status/backup");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/vnd.sqlite3", download.Content.Headers.ContentType?.MediaType);
        var backup = await download.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("SQLite format 3", System.Text.Encoding.ASCII.GetString(backup, 0, 15), StringComparison.Ordinal);

        var restore = await UploadAsync(admin, backup);

        Assert.Equal("/status?restore=staged", TestApp.RedirectTarget(restore));
        Assert.Contains("waits for the next start", await admin.GetStringAsync("/status?restore=staged"), StringComparison.Ordinal);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<DatabaseBackups>().RestorePending);
    }

    [Fact]
    public async Task Upload_that_is_no_backup_is_explained_and_nothing_waits()
    {
        var admin = await _app.SignedInAdminAsync();

        var restore = await UploadAsync(admin, "not a database"u8.ToArray());

        Assert.Equal("/status?restore=NotSqlite", TestApp.RedirectTarget(restore));
        Assert.Contains("That is no database file.", await admin.GetStringAsync("/status?restore=NotSqlite"), StringComparison.Ordinal);
        await using var scope = _app.Services.CreateAsyncScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<DatabaseBackups>().RestorePending);
    }

    [Fact]
    public async Task Backup_without_a_token_is_refused()
    {
        var admin = await _app.SignedInAdminAsync();

        var response = await admin.PostAsync("/status/backup", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_who_are_not_admins_can_neither_download_nor_restore()
    {
        await _app.SignedInAdminAsync();
        var user = await _app.SignedInUserAsync("erika");

        var download = await TestApp.SubmitAsync(user, "/", [], action: "/status/backup");
        var restore = await TestApp.SubmitAsync(user, "/", [], action: "/status/restore");

        Assert.NotEqual(HttpStatusCode.OK, download.StatusCode);
        Assert.NotEqual("application/vnd.sqlite3", download.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("restore=", TestApp.RedirectTarget(restore) ?? string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, restore.StatusCode);
    }

    public void Dispose() => _app.Dispose();

    // Like the browser: the restore form as multipart, with the token the status page rendered into it.
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient browser, byte[] file)
    {
        var page = await browser.GetStringAsync("/status");
        var token = Token().Match(page).Groups["value"].Value;
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(WebUtility.HtmlDecode(token)), "__RequestVerificationToken" },
            { content, "backup", "backup.db" },
        };
        return await browser.PostAsync("/status/restore", form);
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<value>[^"]+)""")]
    private static partial Regex Token();
}
