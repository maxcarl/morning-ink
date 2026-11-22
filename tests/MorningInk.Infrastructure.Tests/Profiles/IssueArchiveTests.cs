using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Delivery;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Tests.Delivery;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Profiles;

public sealed class IssueArchiveTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly FakeSmtpServer _smtp = new();

    [Fact]
    public async Task Lists_only_the_users_own_issues_newest_first()
    {
        await using var services = await StartAsync();
        var max = await CreateUserWithProfileAsync(services, "max");
        var erika = await CreateUserWithProfileAsync(services, "erika");
        await AddIssueAsync(services, max.ProfileId, 1, IssueStatus.Delivered, "Erste Meldung");
        await AddIssueAsync(services, max.ProfileId, 2, IssueStatus.Delivered, "Zweite Meldung");
        await AddIssueAsync(services, erika.ProfileId, 1, IssueStatus.Delivered, "Fremde Meldung");

        var page = await ArchiveAsync(services, a => a.ListAsync(max.UserId, null, 10, CancellationToken.None));

        Assert.Equal([2, 1], page.Issues.Select(i => i.Issue.Number));
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Search_finds_issues_by_article_title_ignoring_case_and_wildcards()
    {
        await using var services = await StartAsync();
        var max = await CreateUserWithProfileAsync(services, "max");
        await AddIssueAsync(services, max.ProfileId, 1, IssueStatus.Delivered, "Raumfahrt: Start geglückt");
        await AddIssueAsync(services, max.ProfileId, 2, IssueStatus.Delivered, "Wetter am Wochenende");

        var found = await ArchiveAsync(services, a => a.ListAsync(max.UserId, "RAUMFAHRT", 10, CancellationToken.None));
        var wildcard = await ArchiveAsync(services, a => a.ListAsync(max.UserId, "%", 10, CancellationToken.None));

        Assert.Equal([1], found.Issues.Select(i => i.Issue.Number));
        Assert.Empty(wildcard.Issues);
    }

    [Fact]
    public async Task Failed_issue_sent_again_arrives_and_counts_as_delivered()
    {
        await using var services = await StartAsync();
        var max = await CreateUserWithProfileAsync(services, "max");
        var issueId = await AddIssueAsync(services, max.ProfileId, 7, IssueStatus.Failed, "Erste Meldung");

        var result = await ArchiveAsync(services, a => a.ResendAsync(max.UserId, issueId, CancellationToken.None));

        Assert.IsType<DeliveryResult.Delivered>(result);
        var message = Assert.Single(_smtp.Messages);
        Assert.Equal("Morning Ink 2026-11-20", message.Subject);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var issue = await db.Issues.Include(i => i.Attempts).SingleAsync(i => i.Id == issueId);
        Assert.Equal(IssueStatus.Delivered, issue.Status);
        Assert.Null(issue.Error);
        Assert.True(Assert.Single(issue.Attempts).Succeeded);
        Assert.Equal(1, await db.SeenArticles.CountAsync()); // its articles do not come again
    }

    [Fact]
    public async Task Pinning_keeps_an_issue_and_unpinning_releases_it()
    {
        await using var services = await StartAsync();
        var max = await CreateUserWithProfileAsync(services, "max");
        var issueId = await AddIssueAsync(services, max.ProfileId, 1, IssueStatus.Delivered, "Erste Meldung");

        Assert.True(await ArchiveAsync(services, a => a.SetPinnedAsync(max.UserId, issueId, true, CancellationToken.None)));
        var pinned = (await ArchiveAsync(services, a => a.ListAsync(max.UserId, null, 10, CancellationToken.None))).Issues.Single().Issue.Pinned;
        Assert.True(await ArchiveAsync(services, a => a.SetPinnedAsync(max.UserId, issueId, false, CancellationToken.None)));
        var unpinned = (await ArchiveAsync(services, a => a.ListAsync(max.UserId, null, 10, CancellationToken.None))).Issues.Single().Issue.Pinned;

        Assert.True(pinned);
        Assert.False(unpinned);
    }

    [Fact]
    public async Task Another_users_issue_cannot_be_downloaded_sent_or_pinned()
    {
        await using var services = await StartAsync();
        var max = await CreateUserWithProfileAsync(services, "max");
        var erika = await CreateUserWithProfileAsync(services, "erika");
        var issueId = await AddIssueAsync(services, max.ProfileId, 1, IssueStatus.Delivered, "Erste Meldung");

        Assert.Null(await ArchiveAsync(services, a => a.GetFileAsync(erika.UserId, issueId, CancellationToken.None)));
        Assert.Null(await ArchiveAsync(services, a => a.ResendAsync(erika.UserId, issueId, CancellationToken.None)));
        Assert.False(await ArchiveAsync(services, a => a.SetPinnedAsync(erika.UserId, issueId, true, CancellationToken.None)));
        Assert.Empty(_smtp.Messages);
    }

    [Fact]
    public async Task Download_is_named_like_the_issue()
    {
        await using var services = await StartAsync();
        var max = await CreateUserWithProfileAsync(services, "max");
        var issueId = await AddIssueAsync(services, max.ProfileId, 1, IssueStatus.Delivered, "Erste Meldung");

        var file = await ArchiveAsync(services, a => a.GetFileAsync(max.UserId, issueId, CancellationToken.None));

        Assert.NotNull(file);
        Assert.Equal("Morning Ink 2026-11-20.epub", file.DownloadName);
        Assert.Equal("application/epub+zip", file.MediaType);
        Assert.True(File.Exists(file.Path));
    }

    public void Dispose()
    {
        _host.Dispose();
        _smtp.Dispose();
    }

    // ProfileLocks as in Web's AddApplication; Infrastructure tests cannot reference the Web project.
    private Task<ServiceProvider> StartAsync() => _host.StartAsync(
        new()
        {
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = _smtp.Port.ToString(CultureInfo.InvariantCulture),
            ["Smtp:Security"] = "None",
            ["Smtp:FromAddress"] = "morning-ink@example.com",
        },
        services => services.AddSingleton<ProfileLocks>());

    private static async Task<T> ArchiveAsync<T>(IServiceProvider services, Func<IssueArchive, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IssueArchive>());
    }

    private static async Task<(Guid UserId, Guid ProfileId)> CreateUserWithProfileAsync(IServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = name };
        var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = $"{name}_abc@kindle.com" };
        db.AddRange(user, profile);
        await db.SaveChangesAsync();
        return (user.Id, profile.Id);
    }

    // A rendered issue of 2026-11-20 6:00 in Berlin with one article and a file on disk.
    private static async Task<Guid> AddIssueAsync(IServiceProvider services, Guid profileId, int number, IssueStatus status, string articleTitle)
    {
        await using var scope = services.CreateAsyncScope();
        var slot = new DateTimeOffset(2026, 11, 20, 5, 0, 0, TimeSpan.Zero).AddMinutes(number); // distinct slots, same day
        var issue = new Issue { ProfileId = profileId, Number = number, ScheduledFor = slot, CreatedAt = slot, Status = status };
        issue.FileName = $"{issue.Id}.epub";
        issue.Entries.Add(new IssueEntry { Url = $"https://example.com/{issue.Id}", Title = articleTitle, SourceName = "Beispiel" });
        await File.WriteAllTextAsync(scope.ServiceProvider.GetRequiredService<IIssueFiles>().PathFor(issue.FileName), "epub");
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        db.Issues.Add(issue);
        await db.SaveChangesAsync();
        return issue.Id;
    }
}
