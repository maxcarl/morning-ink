using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Identity;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;

namespace MorningInk.Web.Tests;

/// <summary>E-readers like KOReader fetch issues from an OPDS catalog, signed in with a reader password of their own.</summary>
public sealed partial class OpdsTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Catalog_lists_the_own_issues_and_hands_out_their_files()
    {
        await _app.SignedInAdminAsync();
        var own = await AddIssueAsync(TestApp.AdminName);
        await _app.SignedInUserAsync("erika");
        var foreign = await AddIssueAsync("erika");
        var reader = Reader(TestApp.AdminName, await ReaderPasswordAsync(TestApp.AdminName));

        var catalog = await reader.GetAsync("/opds");
        var feed = await catalog.Content.ReadAsStringAsync();
        var file = await reader.GetAsync($"/opds/issues/{own}");
        var foreignFile = await reader.GetAsync($"/opds/issues/{foreign}");

        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Equal("application/atom+xml", catalog.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Morning Ink 2026-11-20", feed, StringComparison.Ordinal);
        Assert.Contains($"href=\"/opds/issues/{own}\"", feed, StringComparison.Ordinal);
        Assert.Contains($"rel=\"http://opds-spec.org/image/thumbnail\" href=\"/opds/issues/{own}/cover\"", feed, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign.ToString(), feed, StringComparison.Ordinal);
        Assert.Equal("application/epub+zip", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, foreignFile.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reader.GetAsync($"/opds/issues/{foreign}/cover")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Browser().GetAsync($"/opds/issues/{own}/cover")).StatusCode);
    }

    [Fact]
    public async Task Catalog_needs_the_reader_password_not_the_account_password()
    {
        await _app.SignedInAdminAsync();
        await ReaderPasswordAsync(TestApp.AdminName);

        var none = await _app.Browser().GetAsync("/opds");
        var account = await Reader(TestApp.AdminName, TestApp.AdminPassword).GetAsync("/opds");

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal("Basic", Assert.Single(none.Headers.WwwAuthenticate).Scheme); // so the reader asks for name and password
        Assert.Equal(HttpStatusCode.Unauthorized, account.StatusCode);
    }

    [Fact]
    public async Task Account_page_shows_a_new_reader_password_once_and_a_new_one_ends_the_old()
    {
        var browser = await _app.SignedInAdminAsync();

        var first = await (await TestApp.SubmitAsync(browser, "/account", [], form: ("reader", null))).Content.ReadAsStringAsync();
        var firstPassword = ShownPassword().Match(first).Groups["password"].Value;
        var second = await (await TestApp.SubmitAsync(browser, "/account", [], form: ("reader", null))).Content.ReadAsStringAsync();
        var secondPassword = ShownPassword().Match(second).Groups["password"].Value;
        var later = await browser.GetStringAsync("/account");

        Assert.NotEmpty(firstPassword);
        Assert.NotEqual(firstPassword, secondPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Reader(TestApp.AdminName, firstPassword).GetAsync("/opds")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Reader(TestApp.AdminName, secondPassword).GetAsync("/opds")).StatusCode);
        Assert.DoesNotContain(secondPassword, later, StringComparison.Ordinal); // shown once, stored only as a hash
    }

    [Fact]
    public async Task Behind_a_proxy_with_forwarded_headers_on_the_catalog_address_is_the_public_one()
    {
        using var app = new TestApp(new() { ["FORWARDEDHEADERS_ENABLED"] = "true" });
        var browser = await app.SignedInAdminAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Host = "news.example.com";

        var page = await (await browser.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("https://news.example.com/opds", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guessing_the_reader_password_is_slowed_down()
    {
        await _app.SignedInAdminAsync();
        var guesser = Reader(TestApp.AdminName, "wrong");

        var answers = new List<HttpStatusCode>();
        for (var i = 0; i <= SignInLimits.AttemptsPerMinute; i++)
        {
            answers.Add((await guesser.GetAsync("/opds")).StatusCode);
        }

        // The setup's own sign-in counted too: the limit is per client, whatever it signs in to.
        Assert.Equal(HttpStatusCode.Unauthorized, answers[0]);
        Assert.Equal(HttpStatusCode.TooManyRequests, answers[^1]);
    }

    public void Dispose() => _app.Dispose();

    private HttpClient Reader(string name, string password)
    {
        var client = _app.Browser();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{name}:{password}")));
        return client;
    }

    private async Task<string> ReaderPasswordAsync(string name)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var userId = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Where(u => u.Name == name).Select(u => u.Id).SingleAsync();
        return await scope.ServiceProvider.GetRequiredService<ReaderAccess>().CreateAsync(userId, CancellationToken.None);
    }

    private async Task<Guid> AddIssueAsync(string owner)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var userId = await db.DomainUsers.Where(u => u.Name == owner).Select(u => u.Id).SingleAsync();
        var profile = await scope.ServiceProvider.GetRequiredService<ProfileEditor>().GetOrCreateAsync(userId, CancellationToken.None);
        var slot = new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero);
        var issue = new Issue { ProfileId = profile.Id, Number = 1, ScheduledFor = slot, CreatedAt = slot, Status = IssueStatus.Delivered, DeliveredAt = slot };
        issue.FileName = $"{issue.Id}.epub";
        await File.WriteAllTextAsync(scope.ServiceProvider.GetRequiredService<IIssueFiles>().PathFor(issue.FileName), "epub");
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return issue.Id;
    }

    [GeneratedRegex("<code id=\"reader-password\">(?<password>[^<]+)</code>")]
    private static partial Regex ShownPassword();
}
