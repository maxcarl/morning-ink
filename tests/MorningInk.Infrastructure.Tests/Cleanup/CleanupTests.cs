using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Cleanup;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Cleanup;

/// <summary>Default retention: issues 30 days, seen articles 90 days, images 7 days, 5 backups.</summary>
public sealed class CleanupTests : IDisposable
{
    private readonly TestHost _host = new();

    private string IssueDirectory => Path.Combine(_host.DataDirectory, "issues");

    private string ImageDirectory => Path.Combine(_host.DataDirectory, "images");

    [Fact]
    public async Task Old_issues_go_with_their_files_and_pinned_ones_stay()
    {
        await using var services = await _host.StartAsync();
        var profileId = await CreateProfileAsync(services);
        var old = await CreateIssueAsync(services, profileId, ageInDays: 40);
        var pinned = await CreateIssueAsync(services, profileId, ageInDays: 40, pinned: true);
        var recent = await CreateIssueAsync(services, profileId, ageInDays: 5);

        await RunAsync(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        // Both sides sorted: ids made in the same millisecond have no fixed order, which made this test flaky.
        Assert.Equal(new[] { pinned, recent }.Order(), (await db.Issues.Select(i => i.Id).ToListAsync()).Order());
        Assert.Empty(await db.Set<IssueEntry>().Where(e => e.IssueId == old).ToListAsync());
        Assert.False(File.Exists(Path.Combine(IssueDirectory, $"{old}.epub")));
        Assert.True(File.Exists(Path.Combine(IssueDirectory, $"{pinned}.epub")));
        Assert.True(File.Exists(Path.Combine(IssueDirectory, $"{recent}.epub")));
    }

    [Fact]
    public async Task Old_seen_articles_are_forgotten()
    {
        await using var services = await _host.StartAsync();
        await CreateProfileAsync(services);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var user = await db.DomainUsers.SingleAsync();
            db.SeenArticles.AddRange(
                new SeenArticle { UserId = user.Id, UrlHash = new string('a', 64), SeenAt = TestHost.Now.AddDays(-100) },
                new SeenArticle { UserId = user.Id, UrlHash = new string('b', 64), SeenAt = TestHost.Now.AddDays(-10) });
            await db.SaveChangesAsync();
        }

        await RunAsync(services);

        await using var check = services.CreateAsyncScope();
        Assert.Equal(new string('b', 64), (await check.ServiceProvider.GetRequiredService<MorningInkDbContext>().SeenArticles.SingleAsync()).UrlHash);
    }

    [Fact]
    public async Task Ai_calls_are_kept_as_long_as_issues()
    {
        await using var services = await _host.StartAsync();
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            db.AiCalls.AddRange(Call("Alt", TestHost.Now.AddDays(-40)), Call("Neu", TestHost.Now.AddDays(-5)));
            await db.SaveChangesAsync();
        }

        await RunAsync(services);

        await using var check = services.CreateAsyncScope();
        Assert.Equal("Neu", (await check.ServiceProvider.GetRequiredService<MorningInkDbContext>().AiCalls.SingleAsync()).ArticleTitle);
    }

    private static AiCall Call(string title, DateTimeOffset at) => new()
    {
        At = at,
        Provider = "127.0.0.1:11434",
        Model = "qwen2.5:7b",
        UserId = Guid.NewGuid(),
        SourceName = "Feed",
        ArticleTitle = title,
        ArticleUrl = "https://example.com/",
    };

    [Fact]
    public async Task Old_images_go_and_recent_ones_stay()
    {
        await using var services = await _host.StartAsync();
        Directory.CreateDirectory(ImageDirectory);
        var oldImage = WriteFile(ImageDirectory, "old.jpg", TestHost.Now.AddDays(-8));
        var recentImage = WriteFile(ImageDirectory, "recent.jpg", TestHost.Now.AddDays(-1));

        await RunAsync(services);

        Assert.False(File.Exists(oldImage));
        Assert.True(File.Exists(recentImage));
    }

    [Fact]
    public async Task Only_the_newest_backups_are_kept()
    {
        await using var services = await _host.StartAsync();
        Directory.CreateDirectory(_host.BackupDirectory);
        for (var day = 1; day <= 7; day++)
        {
            WriteFile(_host.BackupDirectory, $"morning-ink-202611{day:00}-060000.db", TestHost.Now);
        }

        await RunAsync(services);

        Assert.Equal(
            ["morning-ink-20261103-060000.db", "morning-ink-20261104-060000.db", "morning-ink-20261105-060000.db", "morning-ink-20261106-060000.db", "morning-ink-20261107-060000.db"],
            Directory.GetFiles(_host.BackupDirectory).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Cleanup_without_anything_to_do_is_harmless()
    {
        await using var services = await _host.StartAsync();

        var report = await RunAsync(services);

        Assert.Equal(new CleanupReport(0, 0, 0, 0), report);
    }

    public void Dispose() => _host.Dispose();

    private static async Task<CleanupReport> RunAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CleanupService>().RunAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateProfileAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "alex" };
        var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = "alex@kindle.com" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private async Task<Guid> CreateIssueAsync(IServiceProvider services, Guid profileId, int ageInDays, bool pinned = false)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var created = TestHost.Now.AddDays(-ageInDays);
        var number = await db.Issues.CountAsync() + 1;
        var issue = new Issue
        {
            ProfileId = profileId,
            Number = number,
            ScheduledFor = created.AddMinutes(number), // one issue per slot, so each gets its own
            CreatedAt = created,
            Status = IssueStatus.Delivered,
            Pinned = pinned,
        };
        issue.FileName = $"{issue.Id}.epub";
        issue.Entries.Add(new IssueEntry { Url = "https://example.com/a", Title = "A", SourceName = "S" });
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        Directory.CreateDirectory(IssueDirectory);
        WriteFile(IssueDirectory, issue.FileName, created);
        return issue.Id;
    }

    private static string WriteFile(string directory, string name, DateTimeOffset modified)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, modified.UtcDateTime);
        return path;
    }
}
