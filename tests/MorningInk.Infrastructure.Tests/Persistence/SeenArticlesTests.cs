using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Tests.Persistence;

public sealed class SeenArticlesTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task A_second_run_right_after_yields_no_new_articles()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services);
        var batch = new[] { Article("https://example.com/a"), Article("https://example.com/b") };

        var first = await FilterAndMarkAsync(services, user, batch);
        var second = await FilterAndMarkAsync(services, user, batch);

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
    }

    [Fact]
    public async Task Tracking_variants_of_a_seen_article_are_not_new()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services);
        await FilterAndMarkAsync(services, user, [Article("https://www.example.com/a?utm_source=rss")]);

        var again = await FilterAndMarkAsync(services, user, [Article("http://example.com/a/#top")]);

        Assert.Empty(again);
    }

    [Fact]
    public async Task The_same_article_from_two_feeds_appears_once()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services);

        var result = await FilterAndMarkAsync(services, user, [Article("https://example.com/a?utm_source=feed1"), Article("https://example.com/a?utm_source=feed2")]);

        Assert.Single(result);
    }

    [Fact]
    public async Task Articles_count_as_seen_only_after_marking()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services);
        var batch = new[] { Article("https://example.com/a") };

        await using var scope = services.CreateAsyncScope();
        var seen = scope.ServiceProvider.GetRequiredService<ISeenArticles>();
        await seen.FilterNewAsync(user, batch, CancellationToken.None);

        // Not marked, e.g. because the delivery failed: the article must come again.
        Assert.Single(await seen.FilterNewAsync(user, batch, CancellationToken.None));
    }

    [Fact]
    public async Task Each_user_has_their_own_list()
    {
        await using var services = await _host.StartAsync();
        var alice = await CreateUserAsync(services, "alice");
        var bob = await CreateUserAsync(services, "bob");
        var batch = new[] { Article("https://example.com/a") };
        await FilterAndMarkAsync(services, alice, batch);

        Assert.Single(await FilterAndMarkAsync(services, bob, batch));
    }

    [Fact]
    public async Task Marking_twice_is_harmless()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services);
        var batch = new[] { Article("https://example.com/a") };

        await using var scope = services.CreateAsyncScope();
        var seen = scope.ServiceProvider.GetRequiredService<ISeenArticles>();
        await seen.MarkSeenAsync(user, batch.Select(a => a.Url), CancellationToken.None);
        await seen.MarkSeenAsync(user, batch.Select(a => a.Url), CancellationToken.None);

        Assert.Empty(await seen.FilterNewAsync(user, batch, CancellationToken.None));
    }

    public void Dispose() => _host.Dispose();

    private static FetchedArticle Article(string url) => new(null, url, new Uri(url), null, null, null, null);

    private static async Task<Guid> CreateUserAsync(IServiceProvider services, string name = "max")
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = name };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<IReadOnlyList<FetchedArticle>> FilterAndMarkAsync(IServiceProvider services, Guid userId, IReadOnlyList<FetchedArticle> articles)
    {
        // A fresh scope per run, like separate issue runs.
        await using var scope = services.CreateAsyncScope();
        var seen = scope.ServiceProvider.GetRequiredService<ISeenArticles>();
        var fresh = await seen.FilterNewAsync(userId, articles, CancellationToken.None);
        await seen.MarkSeenAsync(userId, fresh.Select(a => a.Url), CancellationToken.None);
        return fresh;
    }
}
