using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Publishing;
using MorningInk.Application.Rendering;
using MorningInk.Domain;
using MorningInk.Infrastructure.Feedback;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Feedback;

/// <summary>"More of this" and "less of this" from the reader: signed links in the issue, recorded per article.</summary>
public sealed class FeedbackTests : IDisposable
{
    private const string PublicUrl = "https://news.example.com";

    private readonly TestHost _host = new();

    [Fact]
    public async Task A_link_from_the_issue_is_recorded_once_per_article_and_the_last_word_counts()
    {
        await using var services = await StartAsync();
        var (entry, profile, source) = await IssueEntryAsync(services);
        var links = Links(services, entry);

        var first = await RecordAsync(services, links.More);
        var second = await RecordAsync(services, links.Less); // changed their mind

        Assert.Equal((FeedbackKind.More, "Neuer Raspberry Pi"), (first.Kind, first.ArticleTitle));
        Assert.Equal(FeedbackKind.Less, second.Kind);
        await using var scope = services.CreateAsyncScope();
        var saved = await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Feedback.SingleAsync();
        Assert.Equal((profile, source, FeedbackKind.Less), (saved.ProfileId, saved.SourceId, saved.Kind));
        Assert.StartsWith($"{PublicUrl}/feedback/", links.More.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_changed_link_is_refused()
    {
        await using var services = await StartAsync();
        var (entry, _, _) = await IssueEntryAsync(services);
        var token = Token(Links(services, entry).More);

        var result = await RecordAsync(services, new Uri($"{PublicUrl}/feedback/{token[..^4]}AAAA"));

        Assert.Null(result.Kind);
    }

    [Fact]
    public async Task A_link_older_than_its_lifetime_is_refused()
    {
        Uri link;
        await using (var services = await StartAsync())
        {
            var (entry, _, _) = await IssueEntryAsync(services);
            link = Links(services, entry).More;
        }

        // The same instance, with its keys, two months later.
        await using var later = await _host.StartAsync(
            new() { ["Web:PublicUrl"] = PublicUrl },
            services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestHost.Now.AddDays(61))));

        Assert.Null((await RecordAsync(later, link)).Kind);
    }

    [Fact]
    public async Task Without_a_public_address_there_are_no_links()
    {
        await using var services = await _host.StartAsync();
        var (entry, _, _) = await IssueEntryAsync(services);

        Assert.Null(services.GetRequiredService<IFeedbackLinks>().LinksFor(entry));
    }

    [Fact]
    public async Task The_last_two_months_of_feedback_lean_the_edition_towards_its_sources()
    {
        await using var services = await StartAsync();
        var (entry, profileId, source) = await IssueEntryAsync(services);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var userId = await db.Profiles.Where(p => p.Id == profileId).Select(p => p.UserId).SingleAsync();
            db.Feedback.AddRange(
                Feedback(userId, profileId, source, "a", FeedbackKind.More, daysAgo: 1),
                Feedback(userId, profileId, source, "b", FeedbackKind.More, daysAgo: 10),
                Feedback(userId, profileId, source, "c", FeedbackKind.Less, daysAgo: 20),
                Feedback(userId, profileId, source, "d", FeedbackKind.Less, daysAgo: 90)); // too old to count
            await db.SaveChangesAsync();
        }

        await using var check = services.CreateAsyncScope();
        var loaded = await check.ServiceProvider.GetRequiredService<IPublishingStore>().LoadProfileAsync(profileId, CancellationToken.None);

        Assert.Equal(1, loaded!.SourceLeanings[source]);
    }

    public void Dispose() => _host.Dispose();

    private Task<ServiceProvider> StartAsync() => _host.StartAsync(new() { ["Web:PublicUrl"] = PublicUrl });

    private static FeedbackLinks Links(IServiceProvider services, Guid entry) =>
        services.GetRequiredService<IFeedbackLinks>().LinksFor(entry)!; // ! is safe: a public address is set

    private static string Token(Uri link) => link.AbsolutePath["/feedback/".Length..];

    private static async Task<FeedbackOutcome> RecordAsync(IServiceProvider services, Uri link)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FeedbackRecorder>().RecordAsync(Token(link), CancellationToken.None);
    }

    private static Domain.Feedback Feedback(Guid userId, Guid profileId, Guid sourceId, string url, FeedbackKind kind, int daysAgo) => new()
    {
        UserId = userId,
        ProfileId = profileId,
        SourceId = sourceId,
        SourceName = "heise",
        ArticleTitle = url,
        ArticleUrl = $"https://example.com/{url}",
        Kind = kind,
        At = TestHost.Now.AddDays(-daysAgo),
    };

    // A delivered issue with one article, as the publisher leaves it.
    private static async Task<(Guid Entry, Guid Profile, Guid Source)> IssueEntryAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "reader", Language = "de" };
        var source = new Source { UserId = user.Id, Name = "heise", Url = "https://heise.de/rss" };
        var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = string.Empty };
        var issue = new Issue { ProfileId = profile.Id, Number = 1, ScheduledFor = TestHost.Now, CreatedAt = TestHost.Now, Status = IssueStatus.Delivered };
        var entry = new IssueEntry { Url = "https://heise.de/pi", Title = "Neuer Raspberry Pi", SourceName = "heise", SourceId = source.Id };
        issue.Entries.Add(entry);
        db.AddRange(user, source, profile, issue);
        await db.SaveChangesAsync();
        return (entry.Id, profile.Id, source.Id);
    }
}
