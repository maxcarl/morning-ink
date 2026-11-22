using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Sources;

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

/// <summary>
/// Sources behind the user's own account: FreshRSS categories and newsletter folders. Each is checked by signing in
/// before it is saved, so a typo shows now and not in tomorrow's issue.
/// </summary>
public sealed class ConnectedSources(MorningInkDbContext db, FreshRssClient freshRss, NewsletterClient newsletters)
{
    // Both hold what waits to be read, which may be older than a feed's day; seen articles never come twice.
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private const string FreshRssApiPath = "api/greader.php";
    private const string DefaultFolder = "INBOX";
    private const int MaxFolderLength = 200;

    /// <summary>Signs in to FreshRSS and lists its categories, so the user can pick which become sources.</summary>
    /// <param name="address">The instance ("https://freshrss.example.com") or its API ("…/api/greader.php").</param>
    public async Task<FreshRssConnection> ConnectFreshRssAsync(string address, string user, string password, CancellationToken cancellationToken)
    {
        if (FreshRssApi(address) is not { } api)
        {
            return new FreshRssConnection.Failed(new SourceResult.Failure("This is not a valid address.", Reason: nameof(FetchError.InvalidUrl)));
        }

        var (session, failure) = await freshRss.SignInAsync(api, user.Trim(), password, cancellationToken);
        if (session is null)
        {
            return new FreshRssConnection.Failed(failure!); // ! is safe: no session means a failure
        }

        var (categories, listFailure) = await freshRss.CategoriesAsync(api, session, cancellationToken);
        return listFailure is null ? new FreshRssConnection.Connected(api, categories) : new FreshRssConnection.Failed(listFailure);
    }

    /// <summary>One source per chosen category, all with the same login; no choice means one source for everything unread.</summary>
    public async Task<IReadOnlyList<Source>> AddFreshRssAsync(Guid userId, Uri api, string user, string password, IReadOnlyList<FreshRssCategory> categories, CancellationToken cancellationToken)
    {
        var login = await db.SaveLoginAsync(userId, $"FreshRSS {api.Host}", user.Trim(), password, cancellationToken);
        IEnumerable<(string? Stream, string Name)> chosen = categories.Count == 0
            ? [(null, "FreshRSS")]
            : categories.Select(c => ((string?)c.Stream, c.Name));
        var added = chosen.Select(c => new Source
        {
            UserId = userId,
            Name = SourceCatalog.FitName(c.Name),
            Kind = SourceKind.FreshRss,
            Url = api.AbsoluteUri,
            Stream = c.Stream,
            CredentialId = login.Id,

            // FreshRSS already holds the full text it fetched.
            FetchFullText = false,
            MaxAge = MaxAge,
        }).ToList();
        db.Sources.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    /// <returns>The new source, or why not: "NameRequired", "NewsletterServer", or what the mail server said.</returns>
    public async Task<(Source? Source, SourceResult.Failure? Failure)> AddNewsletterAsync(Guid userId, NewsletterInput input, CancellationToken cancellationToken)
    {
        var name = input.Name.Trim();
        var folder = string.IsNullOrWhiteSpace(input.Folder) ? DefaultFolder : input.Folder.Trim();
        var sender = string.IsNullOrWhiteSpace(input.Sender) ? null : input.Sender.Trim();
        if (name.Length is 0 or > SourceCatalog.MaxNameLength)
        {
            return (null, new SourceResult.Failure("A name is required.", Reason: "NameRequired"));
        }

        if (NewsletterClient.ParseServer(input.Server) is not var (host, port) || folder.Length > MaxFolderLength || sender?.Length > SourceCatalog.MaxNameLength)
        {
            return (null, new SourceResult.Failure("That is no mail server.", Reason: "NewsletterServer"));
        }

        var user = input.User.Trim();
        if (await newsletters.CheckAsync(new NewsletterMailbox(host, port, user, input.Password, folder, sender), cancellationToken) is { } failure)
        {
            return (null, failure);
        }

        var login = await db.SaveLoginAsync(userId, $"IMAP {host}", user, input.Password, cancellationToken);
        var source = new Source
        {
            UserId = userId,
            Name = name,
            Kind = SourceKind.Newsletter,
            Url = new UriBuilder("imap", host, port).Uri.AbsoluteUri,
            Stream = folder,
            Sender = sender,
            CredentialId = login.Id,

            // The mail is the article; there is no web page to fetch.
            FetchFullText = false,
            MaxAge = MaxAge,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync(cancellationToken);
        return (source, null);
    }

    // The instance's address leads to its API; an address that already ends in the API is taken as it is.
    private static Uri? FreshRssApi(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || UrlPolicy.Check(uri) is not null || !string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        var root = uri.GetLeftPart(UriPartial.Authority);
        return path.EndsWith("greader.php", StringComparison.OrdinalIgnoreCase) ? new Uri(root + path) : new Uri($"{root}{path}/{FreshRssApiPath}");
    }
}
