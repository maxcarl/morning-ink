using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Profiles;

/// <summary>The statistics page: what the last 30 days of issues tell, read from their log and entries.</summary>
public sealed class IssueStatisticsTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task Issues_sources_and_ai_of_the_last_30_days_are_summed_up()
    {
        await using var services = await _host.StartAsync();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "reader" };
        var other = new User { Name = "other" };
        db.DomainUsers.AddRange(user, other);
        var heise = new Source { UserId = user.Id, Name = "heise", Url = "https://heise.de/rss" };
        var quiet = new Source { UserId = user.Id, Name = "Still", Url = "https://still.example.com/rss" };
        db.Sources.AddRange(heise, quiet);
        var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = "name_abc@kindle.com" };
        var foreign = new Profile { UserId = other.Id, Title = "Fremd", Recipient = "other@kindle.com" };
        db.Profiles.AddRange(profile, foreign);

        db.Issues.AddRange(
            Issue(profile, daysAgo: 1, IssueStatus.Delivered, 200 * 1024, buildSeconds: 60, fetched: 10, failed: false, inIssue: 3, heise),
            Issue(profile, daysAgo: 2, IssueStatus.Delivered, 400 * 1024, buildSeconds: 120, fetched: 6, failed: false, inIssue: 1, heise),
            Issue(profile, daysAgo: 3, IssueStatus.Failed, null, buildSeconds: null, fetched: 0, failed: true, inIssue: 0, heise),
            Issue(profile, daysAgo: 40, IssueStatus.Delivered, 999 * 1024, buildSeconds: 999, fetched: 99, failed: false, inIssue: 9, heise), // too old
            Issue(foreign, daysAgo: 1, IssueStatus.Delivered, 999 * 1024, buildSeconds: 999, fetched: 99, failed: false, inIssue: 9, heise)); // someone else's
        db.AiCalls.AddRange(
            Call(user.Id, daysAgo: 1, 1000),
            Call(user.Id, daysAgo: 2, 500),
            Call(other.Id, daysAgo: 1, 9999));
        await db.SaveChangesAsync();

        var report = await scope.ServiceProvider.GetRequiredService<IssueStatistics>().LoadAsync(user.Id, CancellationToken.None);

        Assert.Equal((3, 2, 1), (report.Issues, report.Delivered, report.Failed));
        Assert.Equal(300 * 1024, report.AverageBytes);
        Assert.Equal(2.0, report.AverageArticles); // of the issues that got a file
        Assert.Equal(TimeSpan.FromSeconds(90), report.AverageBuildTime);
        Assert.Equal((2, 1500L), (report.AiCalls, report.AiCharacters));
        Assert.Equal(
            [new SourceStatistics("heise", Fetched: 16, InIssues: 4, FailedRuns: 1), new SourceStatistics("Still", 0, 0, 0)],
            report.Sources);
    }

    public void Dispose() => _host.Dispose();

    private static Issue Issue(Profile profile, int daysAgo, IssueStatus status, long? size, int? buildSeconds, int fetched, bool failed, int inIssue, Source source)
    {
        var started = TestHost.Now.AddDays(-daysAgo);
        var issue = new Issue { ProfileId = profile.Id, Number = 1, ScheduledFor = started, CreatedAt = started, Status = status, FileSize = size };
        issue.AddLog(started, "Started");
        issue.AddLog(started, failed ? "SourceFailed" : "Fetched", failed ? [source.Name, "down", "Unreachable", "heise.de"] : [source.Name, fetched]);
        if (buildSeconds is { } seconds)
        {
            issue.AddLog(started.AddSeconds(seconds), "Rendered", (size ?? 0) / 1024);
        }

        for (var i = 0; i < inIssue; i++)
        {
            issue.Entries.Add(new IssueEntry { Url = $"https://heise.de/{daysAgo}/{i}", Title = "T", SourceName = source.Name, SourceId = source.Id });
        }

        issue.Entries.Add(new IssueEntry { Url = $"https://other.example.com/{daysAgo}", Title = "Doppelt", SourceName = source.Name, SourceId = source.Id, Duplicate = true }); // merged, not an article of its own
        return issue;
    }

    private static AiCall Call(Guid userId, int daysAgo, int characters) => new()
    {
        At = TestHost.Now.AddDays(-daysAgo),
        Provider = "127.0.0.1:11434",
        Model = "qwen2.5:7b",
        UserId = userId,
        SourceName = "heise",
        ArticleTitle = "T",
        ArticleUrl = "https://heise.de/",
        Characters = characters,
        Succeeded = true,
    };
}
