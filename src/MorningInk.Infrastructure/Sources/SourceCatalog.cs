using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Sources;

/// <summary>Outcome of saving a source.</summary>
public abstract record SourceSaveResult
{
    private SourceSaveResult()
    {
    }

    public sealed record Saved(Source Source) : SourceSaveResult;

    /// <param name="Reason">Why it was not saved, as a key the UI turns into text: "NameRequired", "InvalidUrl", "PlaceRequired".</param>
    public sealed record Invalid(string Reason) : SourceSaveResult;
}

/// <summary>A user's sources: list, add, delete and test. Every method only ever sees the given user's sources.</summary>
public sealed class SourceCatalog(MorningInkDbContext db, IEnumerable<ISource> sources)
{
    private const int MaxNameLength = 200;
    private const int MaxPlaceLength = 100;
    private const int LocalNewsMaxArticles = 10;

    public async Task<IReadOnlyList<Source>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Sources.Where(s => s.UserId == userId).OrderBy(s => s.Name).AsNoTracking().ToListAsync(cancellationToken);

    public Task<SourceSaveResult> AddFeedAsync(Guid userId, string name, string url, bool fetchFullText, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var feed) || UrlPolicy.Check(feed) is not null)
        {
            return Task.FromResult<SourceSaveResult>(new SourceSaveResult.Invalid("InvalidUrl"));
        }

        return SaveAsync(new Source { UserId = userId, Name = name.Trim(), Url = feed.AbsoluteUri, FetchFullText = fetchFullText }, cancellationToken);
    }

    public Task<SourceSaveResult> AddLocalAsync(Guid userId, string name, string place, CancellationToken cancellationToken)
    {
        place = place.Trim();
        if (place.Length is 0 or > MaxPlaceLength)
        {
            return Task.FromResult<SourceSaveResult>(new SourceSaveResult.Invalid("PlaceRequired"));
        }

        // Local news are headlines without full text (see FullTextProcessor), so fewer of them.
        return SaveAsync(new Source { UserId = userId, Name = name.Trim(), Kind = SourceKind.Local, Place = place, FetchFullText = false, MaxArticles = LocalNewsMaxArticles }, cancellationToken);
    }

    /// <summary>Deletes the source if it belongs to the user; false otherwise.</summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid sourceId, CancellationToken cancellationToken) =>
        await db.Sources.Where(s => s.Id == sourceId && s.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>Fetches a source that is not saved yet, e.g. to preview a feed before adding it.</summary>
    public async Task<SourceResult> PreviewAsync(Source draft, CancellationToken cancellationToken) =>
        sources.FirstOrDefault(s => s.Kind == draft.Kind) is { } implementation
            ? await implementation.FetchAsync(draft, cancellationToken)
            : new SourceResult.Failure($"Source type {draft.Kind} is not available.");

    /// <summary>Fetches a saved source of the user ("test now"); null if it does not belong to them.</summary>
    /// <remarks>Runs on a detached copy without cache validators, so the test always shows articles and never changes what the next issue fetches.</remarks>
    public async Task<SourceResult?> TestAsync(Guid userId, Guid sourceId, CancellationToken cancellationToken)
    {
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        source.ETag = null;
        source.LastModified = null;
        return await PreviewAsync(source, cancellationToken);
    }

    private async Task<SourceSaveResult> SaveAsync(Source source, CancellationToken cancellationToken)
    {
        if (source.Name.Length is 0 or > MaxNameLength)
        {
            return new SourceSaveResult.Invalid("NameRequired");
        }

        db.Sources.Add(source);
        await db.SaveChangesAsync(cancellationToken);
        return new SourceSaveResult.Saved(source);
    }
}
