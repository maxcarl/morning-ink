using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using MorningInk.Domain;
using MorningInk.Infrastructure.Transfer;

namespace MorningInk.Web;

/// <summary>
/// Downloading one's setup as JSON from the status page; the import runs on the page itself, which shows what happened.
/// A form post with an antiforgery token, like the backup.
/// </summary>
public static class ConfigEndpoints
{
    private const string JsonMediaType = "application/json";

    public static void MapConfigTransfer(this WebApplication app) =>
        app.MapPost("/status/config", async (HttpContext context, IAntiforgery antiforgery, ConfigTransfer transfer, TimeProvider time) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            var user = context.User;
            var json = await transfer.ExportAsync(user.Id(), user.IsInRole(nameof(UserRole.Admin)), context.RequestAborted);

            // Named by the date alone, never by anything a user typed.
            var date = time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Results.File(Encoding.UTF8.GetBytes(json), JsonMediaType, $"morning-ink-config-{date}.json");
        });
}
