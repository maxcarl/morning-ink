using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using MorningInk.Application.Rendering;
using MorningInk.Application.Scheduling;
using MorningInk.Infrastructure.Identity;
using MorningInk.Infrastructure.Profiles;

namespace MorningInk.Web;

/// <summary>
/// OPDS catalog for e-readers such as KOReader: the user's issues with their files, newest first. Readers sign in with
/// HTTP Basic and the reader password from the account page, never with the account's password or the cookie.
/// </summary>
/// <remarks>Basic sends the password with every request: use it over https, or only inside the home network.</remarks>
public static class Opds
{
    private const string AcquisitionFeed = "application/atom+xml;profile=opds-catalog;kind=acquisition";
    private const string Acquisition = "http://opds-spec.org/acquisition";
    private const string Challenge = "Basic realm=\"Morning Ink\", charset=\"UTF-8\"";
    private const int Issues = 30;

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    // One instance serves all: it holds no state.
    private static readonly IResult Unauthorized = new ChallengeResult();

    public static void MapOpds(this WebApplication app)
    {
        // Anonymous for the cookie login only: each endpoint checks the reader password itself.
        app.MapGet("/opds", async (HttpContext context, ReaderAccess readers, IssueArchive archive) =>
            await ReaderAsync(context, readers) is not { } userId
                ? Unauthorized
                : Results.Text(Feed(userId, await archive.ListAsync(userId, null, Issues, context.RequestAborted)), AcquisitionFeed, Encoding.UTF8))
            .AllowAnonymous();

        app.MapGet("/opds/issues/{issueId:guid}", async (Guid issueId, HttpContext context, ReaderAccess readers, IssueArchive archive) =>
        {
            if (await ReaderAsync(context, readers) is not { } userId)
            {
                return Unauthorized;
            }

            // Not the user's or no longer there: the same answer, so the URL does not tell whether an issue exists.
            return await archive.GetFileAsync(userId, issueId, context.RequestAborted) is { } file
                ? Results.File(file.Path, file.MediaType, file.DownloadName)
                : Results.NotFound();
        }).AllowAnonymous();
    }

    // The reader's user from the Basic header, or null for anything missing, malformed or wrong.
    private static async Task<Guid?> ReaderAsync(HttpContext context, ReaderAccess readers)
    {
        if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is null)
        {
            return null;
        }

        string credentials;
        try
        {
            credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return null;
        }

        var colon = credentials.IndexOf(':', StringComparison.Ordinal);
        return colon <= 0 ? null : await readers.CheckAsync(credentials[..colon], credentials[(colon + 1)..], context.RequestAborted);
    }

    private static string Feed(Guid userId, ArchivePage page)
    {
        var issues = page.Issues.Where(i => i.FileAvailable).ToList();
        var updated = issues.Select(i => i.Issue.DeliveredAt ?? i.Issue.CreatedAt).DefaultIfEmpty(DateTimeOffset.UnixEpoch).Max();
        var feed = new XElement(Atom + "feed",
            new XElement(Atom + "id", $"urn:uuid:{userId}"),
            new XElement(Atom + "title", "Morning Ink"),
            new XElement(Atom + "updated", updated.ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            new XElement(Atom + "author", new XElement(Atom + "name", "Morning Ink")),
            Link("self", "/opds", AcquisitionFeed),
            Link("start", "/opds", AcquisitionFeed),
            issues.Select(Entry));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), feed).ToString();
    }

    private static XElement Entry(ArchivedIssue item)
    {
        var (issue, profile) = (item.Issue, item.Profile);
        var title = IssueContent.BookTitleFor(profile.Title, TimeZoneInfo.ConvertTime(issue.ScheduledFor, ProfileTimeZone.Find(profile.TimeZoneId)));
        return new XElement(Atom + "entry",
            new XElement(Atom + "id", $"urn:uuid:{issue.Id}"),
            new XElement(Atom + "title", title),
            new XElement(Atom + "updated", (issue.DeliveredAt ?? issue.CreatedAt).ToString("O", System.Globalization.CultureInfo.InvariantCulture)),
            Link(Acquisition, $"/opds/issues/{issue.Id}", "application/epub+zip"));
    }

    private static XElement Link(string rel, string href, string type) =>
        new(Atom + "link", new XAttribute("rel", rel), new XAttribute("href", href), new XAttribute("type", type));

    // 401 with the Basic challenge, so readers ask for name and password.
    private sealed class ChallengeResult : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            httpContext.Response.Headers.WWWAuthenticate = Challenge;

            // A body, so the status code pages do not swap this answer for the HTML "not found" page.
            return httpContext.Response.WriteAsync("Sign in with your user name and the reader password from the account page.");
        }
    }
}
