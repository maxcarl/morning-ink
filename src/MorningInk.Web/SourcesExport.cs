using System.Text;
using MorningInk.Infrastructure.Sources;

namespace MorningInk.Web;

/// <summary>The user's feeds as an OPML file, to take them to another reader; signed in, and only one's own.</summary>
public static class SourcesExport
{
    private const string OpmlMediaType = "text/x-opml";

    public static void MapSourcesExport(this WebApplication app) =>
        app.MapGet("/sources/export.opml", async (HttpContext context, SourceCatalog catalog) =>
        {
            // The fallback policy has already required a signed-in user.
            var userId = context.User.Id();
            var opml = await catalog.ExportOpmlAsync(userId, context.RequestAborted);
            return Results.File(Encoding.UTF8.GetBytes(opml), OpmlMediaType, "morning-ink-feeds.opml");
        });
}
