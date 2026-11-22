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

/// <summary>The outcome of connecting to FreshRSS on the sources page.</summary>
public abstract record FreshRssConnection
{
    private FreshRssConnection()
    {
    }

    /// <param name="Api">The API address found, to add sources with.</param>
    public sealed record Connected(Uri Api, IReadOnlyList<FreshRssCategory> Categories) : FreshRssConnection;

    public sealed record Failed(SourceResult.Failure Failure) : FreshRssConnection;
}

/// <summary>What the sources page enters for a newsletter source.</summary>
/// <param name="Server">"host" or "host:port"; without a port, IMAP with TLS on 993.</param>
/// <param name="Sender">Only mails whose sender address contains this, e.g. "substack.com"; empty takes all.</param>
public sealed record NewsletterInput(string Name, string Server, string User, string Password, string Folder, string? Sender);

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

/// <summary>A user's sources: list, add, delete and test. Every method only ever sees the given user's sources.</summary>
public sealed class SourceCatalog(MorningInkDbContext db, IEnumerable<ISource> sources, FreshRssClient freshRss, NewsletterClient newsletters)
{
    // A weekly newsletter may wait a few days to be read; the seen articles keep it from coming twice.
    private static readonly TimeSpan NewsletterMaxAge = TimeSpan.FromDays(7);
    private const int MaxFolderLength = 200;

    // FreshRSS sources read unread articles, which may wait longer than a feed's day; read ones never come again.
    private static readonly TimeSpan FreshRssMaxAge = TimeSpan.FromDays(7);
    private const string FreshRssApiPath = "api/greader.php";

    private const int MaxNameLength = 200;
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
    /// Adds a newsletter source after signing in and opening the folder once, so a typo shows now and not tomorrow.
    /// The login is stored encrypted and reused for further folders or senders of the same mailbox.
    /// </summary>
    /// <returns>The new source, or why not: "NameRequired", "NewsletterServer", a connection failure or a login failure.</returns>
    public async Task<(Source? Source, SourceResult.Failure? Failure)> AddNewsletterAsync(Guid userId, NewsletterInput input, CancellationToken cancellationToken)
    {
        var name = input.Name.Trim();
        var folder = string.IsNullOrWhiteSpace(input.Folder) ? "INBOX" : input.Folder.Trim();
        var sender = string.IsNullOrWhiteSpace(input.Sender) ? null : input.Sender.Trim();
        if (name.Length is 0 or > MaxNameLength)
        {
            return (null, new SourceResult.Failure("A name is required.", Reason: "NameRequired"));
        }

        if (NewsletterClient.ParseServer(input.Server) is not var (host, port) || folder.Length > MaxFolderLength || sender?.Length > MaxNameLength)
        {
            return (null, new SourceResult.Failure("That is no mail server.", Reason: "NewsletterServer"));
        }

        var username = input.User.Trim();
        if (await newsletters.CheckAsync(new NewsletterMailbox(host, port, username, input.Password, folder, sender), cancellationToken) is { } failure)
        {
            return (null, failure);
        }

        var credentialName = $"IMAP {host}";
        var credential = await db.Credentials.SingleOrDefaultAsync(c => c.UserId == userId && c.Name == credentialName && c.Username == username, cancellationToken);
        if (credential is null)
        {
            credential = new Credential { UserId = userId, Name = credentialName, Username = username, Secret = input.Password, Method = CredentialMethod.Basic };
            db.Credentials.Add(credential);
        }
        else
        {
            credential.Secret = input.Password;
        }

        var source = new Source
        {
            UserId = userId,
            Name = name,
            Kind = SourceKind.Newsletter,
            Url = $"imap://{(host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host)}:{port}",
            Stream = folder,
            Sender = sender,
            CredentialId = credential.Id,

            // The mail is the article: there is no web page to fetch the text from.
            FetchFullText = false,
            MaxAge = NewsletterMaxAge,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync(cancellationToken);
        return (source, null);
    }

    /// <summary>
    /// Adds the feeds of an OPML file as feed sources; ones the user already has, by normalized address, are skipped,
    /// as are addresses that are not http or https. Null when the file is too big or no OPML.
    /// </summary>
    public async Task<OpmlImport?> ImportOpmlAsync(Guid userId, Stream file, CancellationToken cancellationToken)
    {
        // Read one byte past the limit to know a file is too big without reading all of it.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await file.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > Opml.MaxBytes)
            {
                return null;
            }
        }

        if (Opml.Read(buffer.ToArray()) is not { } outlines)
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
                var title = string.IsNullOrEmpty(name) ? feed.Host : name;
                db.Sources.Add(new Source { UserId = userId, Name = title.Length > MaxNameLength ? title[..MaxNameLength] : title, Url = feed.AbsoluteUri, FetchFullText = true });
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
            : settings.Sender?.Trim().Length > MaxNameLength ? "NameRequired"
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

    /// <summary>Signs in to FreshRSS and lists its categories, so the user can pick which become sources.</summary>
    /// <param name="address">The instance ("https://freshrss.example.com") or its API ("…/api/greader.php").</param>
    public async Task<FreshRssConnection> ConnectFreshRssAsync(string address, string user, string password, CancellationToken cancellationToken)
    {
        if (FreshRssApi(address) is not { } api)
        {
            return new FreshRssConnection.Failed(new SourceResult.Failure("This is not a valid address.", Reason: nameof(FetchError.InvalidUrl)));
        }

        var (session, failure) = await freshRss.SignInAsync(api, user.Trim(), password, cancellationToken);
        if (failure is not null)
        {
            return new FreshRssConnection.Failed(failure);
        }

        var (categories, listFailure) = await freshRss.CategoriesAsync(api, session!, cancellationToken); // ! is safe: no failure means a session
        return listFailure is null ? new FreshRssConnection.Connected(api, categories) : new FreshRssConnection.Failed(listFailure);
    }

    /// <summary>
    /// Adds one source per chosen category, all sharing one login; an empty choice adds one source for everything unread.
    /// The login is stored encrypted and reused when the same user connects again.
    /// </summary>
    public async Task<IReadOnlyList<Source>> AddFreshRssAsync(Guid userId, Uri api, string user, string password, IReadOnlyList<FreshRssCategory> categories, CancellationToken cancellationToken)
    {
        var name = $"FreshRSS {api.Host}";
        var username = user.Trim();
        var credential = await db.Credentials.SingleOrDefaultAsync(c => c.UserId == userId && c.Name == name && c.Username == username, cancellationToken);
        if (credential is null)
        {
            credential = new Credential { UserId = userId, Name = name, Username = username, Secret = password, Method = CredentialMethod.Basic };
            db.Credentials.Add(credential);
        }
        else
        {
            credential.Secret = password;
        }

        IEnumerable<(string? Stream, string Name)> chosen = categories.Count == 0
            ? [(null, "FreshRSS")]
            : categories.Select(c => ((string?)c.Stream, c.Name));
        var added = chosen.Select(c => new Source
        {
            UserId = userId,
            Name = c.Name.Length > MaxNameLength ? c.Name[..MaxNameLength] : c.Name,
            Kind = SourceKind.FreshRss,
            Url = api.AbsoluteUri,
            Stream = c.Stream,
            CredentialId = credential.Id,

            // FreshRSS already holds the full text it fetched; asking the web page again would only cost time.
            FetchFullText = false,
            MaxAge = FreshRssMaxAge,
        }).ToList();
        db.Sources.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    // The instance's address leads to its API; an address that already ends in the API is taken as it is.
    private static Uri? FreshRssApi(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || UrlPolicy.Check(uri) is not null || !string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return path.EndsWith("greader.php", StringComparison.OrdinalIgnoreCase)
            ? new Uri(uri.GetLeftPart(UriPartial.Authority) + path)
            : new Uri(uri.GetLeftPart(UriPartial.Authority) + path + "/" + FreshRssApiPath);
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
