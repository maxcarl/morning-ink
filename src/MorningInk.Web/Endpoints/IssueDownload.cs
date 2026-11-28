using MorningInk.Infrastructure.Profiles;

namespace MorningInk.Web;

/// <summary>The one way an issue file and its cover leave the server: signed in, and only the owner's own issues.</summary>
public static class IssueDownload
{
    private const string JpegType = "image/jpeg";

    // An issue's cover never changes, so the browser may keep it; private, as it is the user's own.
    private const string CoverCaching = "private, max-age=604800";

    public static void MapIssueDownload(this WebApplication app)
    {
        // The fallback policy has already required a signed-in user. Not the user's or no longer there: the same
        // answer, so the URL does not tell whether an issue exists.
        app.MapGet("/archive/{issueId:guid}/file", async (Guid issueId, HttpContext context, IssueArchive archive) =>
            await archive.GetFileAsync(context.User.Id(), issueId, context.RequestAborted) is { } file
                ? Results.File(file.Path, file.MediaType, file.DownloadName)
                : Results.NotFound());

        app.MapGet("/archive/{issueId:guid}/cover", async (Guid issueId, HttpContext context, IssueArchive archive) =>
            Cover(await archive.GetCoverAsync(context.User.Id(), issueId, context.RequestAborted), context));
    }

    /// <summary>The small cover as a cacheable JPEG, or not found; shared with the reader catalog.</summary>
    public static IResult Cover(string? path, HttpContext context)
    {
        if (path is null)
        {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = CoverCaching;
        return Results.File(path, JpegType);
    }
}
