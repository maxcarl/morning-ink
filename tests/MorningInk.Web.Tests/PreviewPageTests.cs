using System.IO.Compression;
using System.Net;
using System.Text;
using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class PreviewPageTests : IDisposable
{
    private readonly FeedServer _feeds = new();

    [Fact]
    public async Task Next_issue_is_built_and_shown_without_being_stored_or_sent()
    {
        using var app = new TestApp(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_feeds.Port}" });
        using var pages = await Pages.AsAdminAsync(app);
        await AddSectionWithFeedAsync(app);
        var page = pages.Open<Preview>("Build preview");

        Pages.Button(page, "Build preview").Click();

        Pages.Shows(page, "<iframe");
        Assert.Contains("can still change", page.Markup, StringComparison.Ordinal); // the hint that it is today's state
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        Assert.Empty(await db.Issues.ToListAsync());
        Assert.Empty(await db.SeenArticles.ToListAsync());
    }

    [Fact]
    public async Task Preview_files_are_served_only_to_their_owner_and_may_be_framed_by_the_page()
    {
        using var app = new TestApp();
        var admin = await app.SignedInAdminAsync();
        var other = await app.SignedInUserAsync("erika");
        await StoreAsync(app, TestApp.AdminName, Epub(("OEBPS/title.xhtml", "<html>Titelseite</html>")));

        var own = await admin.GetAsync("/preview/file/title.xhtml");
        var foreign = await other.GetAsync("/preview/file/title.xhtml");
        var outside = await admin.GetAsync("/preview/file/..%2Fmimetype");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains("Titelseite", await own.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("SAMEORIGIN", own.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("script-src 'none'", own.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
    }

    public void Dispose() => _feeds.Dispose();

    private static byte[] Epub(params (string Name, string Text)[] entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return output.ToArray();
    }

    private static async Task StoreAsync(TestApp app, string userName, byte[] epub)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var userId = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().DomainUsers.Where(u => u.Name == userName).Select(u => u.Id).SingleAsync();
        app.Services.GetRequiredService<PreviewStore>().Put(userId, epub);
    }

    private async Task AddSectionWithFeedAsync(TestApp app)
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
}
