using System.Security.Claims;
using MorningInk.Infrastructure.Profiles;

namespace MorningInk.Web;

/// <summary>The one way an issue file leaves the server: signed in, and only the owner's own issues.</summary>
public static class IssueDownload
{
    public static void MapIssueDownload(this WebApplication app) =>
        app.MapGet("/archive/{issueId:guid}/file", async (Guid issueId, HttpContext context, IssueArchive archive) =>
        {
            // The fallback policy has already required a signed-in user, so the id claim is there.
            var userId = Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!); // ! is safe: see above

            // Not the user's or no longer there: the same answer, so the URL does not tell whether an issue exists.
            return await archive.GetFileAsync(userId, issueId, context.RequestAborted) is { } file
                ? Results.File(file.Path, file.MediaType, file.DownloadName)
                : Results.NotFound();
        });
}
