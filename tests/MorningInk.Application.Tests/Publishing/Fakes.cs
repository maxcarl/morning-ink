using System.Text;
using MorningInk.Application.Delivery;
using MorningInk.Application.Processing;
using MorningInk.Application.Publishing;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Tests.Publishing;

/// <summary>In-memory stand-ins for everything the publisher talks to.</summary>
internal sealed class FakeStore(Profile profile, string language = "de") : IPublishingStore
{
    public List<Issue> Issues { get; } = [];

    public int Saves { get; private set; }

    public Task<PublishingProfile?> LoadProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult(profileId == profile.Id ? new PublishingProfile(profile, language) : null);

    public Task<Issue?> FindIssueAsync(Guid profileId, DateTimeOffset scheduledFor, CancellationToken cancellationToken) =>
        Task.FromResult(Issues.SingleOrDefault(i => i.ProfileId == profileId && i.ScheduledFor == scheduledFor));

    public Task<int> NextIssueNumberAsync(Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult(Issues.Where(i => i.ProfileId == profileId).Select(i => i.Number).DefaultIfEmpty(0).Max() + 1);

    public void AddIssue(Issue issue) => Issues.Add(issue);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.CompletedTask;
    }
}

/// <summary>Answers per source id; a missing entry throws, like a buggy source.</summary>
internal sealed class FakeSource : ISource
{
    public Dictionary<Guid, Func<SourceResult>> Results { get; } = [];

    public List<Source> Fetched { get; } = [];

    public SourceKind Kind => SourceKind.Rss;

    public Task<SourceResult> FetchAsync(Source source, CancellationToken cancellationToken)
    {
        Fetched.Add(source);
        return Task.FromResult(Results.TryGetValue(source.Id, out var result)
            ? result()
            : throw new InvalidOperationException("Boom"));
    }
}

internal sealed class FakeSeen : ISeenArticles
{
    public HashSet<string> Seen { get; } = [];

    public Task<IReadOnlyList<FetchedArticle>> FilterNewAsync(Guid userId, IReadOnlyList<FetchedArticle> articles, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FetchedArticle>>(articles.Where(a => !Seen.Contains(a.Url.AbsoluteUri)).DistinctBy(a => a.Url.AbsoluteUri).ToList());

    public Task MarkSeenAsync(Guid userId, IEnumerable<Uri> urls, CancellationToken cancellationToken)
    {
        Seen.UnionWith(urls.Select(u => u.AbsoluteUri));
        return Task.CompletedTask;
    }
}

/// <summary>Appends a marker, so tests see that processing ran; throws for titles containing "crash".</summary>
internal sealed class MarkingProcessor : IProcessor
{
    public Task<FetchedArticle> ProcessAsync(FetchedArticle article, Source source, CancellationToken cancellationToken) =>
        article.Title.Contains("crash", StringComparison.Ordinal)
            ? throw new InvalidOperationException("Processor bug")
            : Task.FromResult(article with { Content = (article.Content ?? string.Empty) + "[processed]" });
}

internal sealed class FakeRenderer : IRenderer
{
    public List<IssueContent> Rendered { get; } = [];

    public string FileExtension => ".epub";

    public string MediaType => "application/epub+zip";

    public async Task RenderAsync(IssueContent issue, Stream output, CancellationToken cancellationToken)
    {
        Rendered.Add(issue);
        await output.WriteAsync(Encoding.UTF8.GetBytes(issue.BookTitle), cancellationToken);
    }
}

internal sealed class FakeDelivery : IDelivery
{
    public Queue<DeliveryResult> Results { get; } = [];

    public List<DeliveryRequest> Requests { get; } = [];

    public Task<DeliveryResult> DeliverAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new DeliveryResult.Delivered());
    }
}

internal sealed class TempIssueFiles : IIssueFiles, IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("morning-ink-issues-").FullName;

    public string PathFor(string fileName) => Path.Combine(_directory, fileName);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
