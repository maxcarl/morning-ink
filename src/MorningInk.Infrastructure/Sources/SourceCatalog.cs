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

/// <summary>What the sources page saves for a source; see <see cref="Source"/> for what each setting does.</summary>
public sealed record SourceSettings(
    string Name,
    bool FetchFullText,
    int MaxArticles,
    IReadOnlyList<string> IncludeKeywords,
    IReadOnlyList<string> ExcludeKeywords,
    int? MinWords,
    bool SkipLiveTickers,
    bool LocalAiOnly = false,
    bool SkipAds = true,
    bool MarkReadAfterDelivery = true,
    bool LoadImages = true,
    string? Sender = null)
{
    private static readonly char[] Separators = [',', ';', '\n', '\r'];

    /// <summary>Keywords as typed: separated by commas, semicolons or lines; blanks and repeats dropped.</summary>
    public static IReadOnlyList<string> ParseKeywords(string? text) =>
        (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

/// <summary>
/// A user's sources: list, add feeds and local news, import and export OPML, edit, delete and test. Every method only
/// ever sees the given user's sources. FreshRSS and newsletters are added through <see cref="ConnectedSources"/>.
/// </summary>
public sealed class SourceCatalog(MorningInkDbContext db, IEnumerable<ISource> sources)
{
    public const int MaxNameLength = 200;

    private const int MaxPlaceLength = 100;
    private const int LocalNewsMaxArticles = 10;
    private const int MaxArticlesLimit = 100;
    private const int MaxMinWords = 10_000;
    private const int MaxKeywords = 50;
    private const int MaxKeywordLength = 100;

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

    /// <summary>
    /// Adds the feeds of an OPML file as feed sources; ones the user already has, by normalized address, are skipped,
    /// as are addresses that are not http or https. Null when the file is too big or no OPML.
    /// </summary>
    public async Task<OpmlImport?> ImportOpmlAsync(Guid userId, Stream file, CancellationToken cancellationToken)
    {
        if (await Opml.ReadAsync(file, cancellationToken) is not { } outlines)
        {
            return null;
        }

        var known = (await db.Sources.Where(s => s.UserId == userId && s.Kind == SourceKind.Rss && s.Url != null).Select(s => s.Url!).ToListAsync(cancellationToken))
            .Select(u => UrlNormalizer.Normalize(new Uri(u)))
            .ToHashSet(StringComparer.Ordinal);
        var (duplicates, invalid) = (0, 0);
        foreach (var (name, url) in outlines)
        {
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var feed) || UrlPolicy.Check(feed) is not null)
            {
                invalid++;
            }
            else if (!known.Add(UrlNormalizer.Normalize(feed)))
            {
                duplicates++;
            }
            else
            {
                db.Sources.Add(new Source { UserId = userId, Name = FitName(string.IsNullOrEmpty(name) ? feed.Host : name), Url = feed.AbsoluteUri });
            }
        }

        var added = db.ChangeTracker.Entries<Source>().Count(e => e.State == EntityState.Added);
        await db.SaveChangesAsync(cancellationToken);
        return new OpmlImport(added, duplicates, invalid);
    }

    /// <summary>The user's feed sources as OPML; local news and FreshRSS categories are no feeds another reader could use.</summary>
    public async Task<string> ExportOpmlAsync(Guid userId, CancellationToken cancellationToken) =>
        Opml.Write(await db.Sources.Where(s => s.UserId == userId && s.Kind == SourceKind.Rss && s.Url != null).OrderBy(s => s.Name).AsNoTracking().ToListAsync(cancellationToken));

    /// <param name="country">ISO code the place is in, e.g. "AT"; empty searches worldwide.</param>
    public Task<SourceSaveResult> AddLocalAsync(Guid userId, string name, string place, string? country, CancellationToken cancellationToken)
    {
        place = place.Trim();
        if (place.Length is 0 or > MaxPlaceLength)
        {
            return Task.FromResult<SourceSaveResult>(new SourceSaveResult.Invalid("PlaceRequired"));
        }

        if (!Countries.TryNormalize(country, out var code))
        {
            return Task.FromResult<SourceSaveResult>(new SourceSaveResult.Invalid("CountryInvalid"));
        }

        // Fewer than a feed: a town's news search also brings regional and sports news from further away.
        return SaveAsync(new Source { UserId = userId, Name = name.Trim(), Kind = SourceKind.Local, Place = place, Country = code, FetchFullText = true, MaxArticles = LocalNewsMaxArticles }, cancellationToken);
    }

    /// <summary>Renames the source and sets its limits and filters.</summary>
    /// <returns>Saved, or Invalid with "NotFound", "NameRequired", "MaxArticlesInvalid", "MinWordsInvalid" or "KeywordsInvalid".</returns>
    public async Task<SourceSaveResult> SaveSettingsAsync(Guid userId, Guid sourceId, SourceSettings settings, CancellationToken cancellationToken)
    {
        var name = settings.Name.Trim();
        var reason = name.Length is 0 or > MaxNameLength ? "NameRequired"
            : settings.MaxArticles is < 1 or > MaxArticlesLimit ? "MaxArticlesInvalid"
            : settings.MinWords is < 1 or > MaxMinWords ? "MinWordsInvalid"
            : new[] { settings.IncludeKeywords, settings.ExcludeKeywords }.Any(k => k.Count > MaxKeywords || k.Any(w => w.Length > MaxKeywordLength)) ? "KeywordsInvalid"
            : settings.Sender?.Trim().Length > MaxNameLength ? "SenderTooLong"
            : null;
        if (reason is not null)
        {
            return new SourceSaveResult.Invalid(reason);
        }

        if (await db.Sources.SingleOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId, cancellationToken) is not { } source)
        {
            return new SourceSaveResult.Invalid("NotFound");
        }

        source.Name = name;
        source.FetchFullText = settings.FetchFullText;
        source.MaxArticles = settings.MaxArticles;
        source.IncludeKeywords = [.. settings.IncludeKeywords];
        source.ExcludeKeywords = [.. settings.ExcludeKeywords];
        source.MinWords = settings.MinWords;
        source.SkipLiveTickers = settings.SkipLiveTickers;
        source.LocalAiOnly = settings.LocalAiOnly;
        source.SkipAds = settings.SkipAds;
        source.MarkReadAfterDelivery = settings.MarkReadAfterDelivery;
        source.LoadImages = settings.LoadImages;
        if (source.Kind == SourceKind.Newsletter)
        {
            source.Sender = string.IsNullOrWhiteSpace(settings.Sender) ? null : settings.Sender.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SourceSaveResult.Saved(source);
    }

    /// <summary>Deletes the source if it belongs to the user; false otherwise.</summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid sourceId, CancellationToken cancellationToken) =>
        await db.Sources.Where(s => s.Id == sourceId && s.UserId == userId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>Fetches a source that is not saved yet, e.g. to preview a feed before adding it.</summary>
    public async Task<SourceResult> PreviewAsync(Source draft, CancellationToken cancellationToken) =>
        sources.FirstOrDefault(s => s.Kind == draft.Kind) is { } implementation
            ? await implementation.FetchAsync(draft, cancellationToken)
            : new SourceResult.Failure($"Source type {draft.Kind} is not available.", Reason: "KindUnavailable", Arguments: [draft.Kind.ToString()]);

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

    /// <summary>A name from elsewhere (a feed, an OPML file, FreshRSS), cut to what a source name may be.</summary>
    internal static string FitName(string name) => name.Length > MaxNameLength ? name[..MaxNameLength] : name;

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
