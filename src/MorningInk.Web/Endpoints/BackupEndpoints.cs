using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Web;

/// <summary>
/// Downloading a database backup and uploading one to restore, for admins on the status page. Both are form posts with
/// an antiforgery token: a download writes a snapshot, and a restore replaces every user's data.
/// </summary>
public static class BackupEndpoints
{
    /// <summary>Query parameter the restore answers with on its way back to the status page.</summary>
    public const string RestoreQuery = "restore";

    /// <summary>Value of <see cref="RestoreQuery"/> when the upload was accepted; otherwise a <see cref="RestoreProblem"/>.</summary>
    public const string Staged = "staged";

    /// <summary>Name of the file field in the restore form.</summary>
    public const string FileField = "backup";

    private const string SqliteMediaType = "application/vnd.sqlite3";

    // Room for the multipart framing and the antiforgery field around the file.
    private const long FormOverheadBytes = 64 * 1024;

    public static void MapBackups(this WebApplication app)
    {
        var admin = app.MapGroup("/status").RequireAuthorization(policy => policy.RequireRole(nameof(UserRole.Admin)));

        admin.MapPost("/backup", async (HttpContext context, IAntiforgery antiforgery, DatabaseBackups backups) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            var path = await backups.CreateAsync(context.RequestAborted);
            return Results.File(path, SqliteMediaType, Path.GetFileName(path));
        });

        admin.MapPost("/restore", async (HttpContext context, IAntiforgery antiforgery, DatabaseBackups backups) =>
        {
            // Before anything reads the form: the antiforgery check does, and the defaults stop at 30 MB.
            var limit = DatabaseBackups.MaxRestoreBytes + FormOverheadBytes;
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
            {
                size.MaxRequestBodySize = limit;
            }

            context.Features.Set<IFormFeature>(new FormFeature(context.Request, new FormOptions { MultipartBodyLengthLimit = limit }));
            if (!await antiforgery.IsRequestValidAsync(context))
            {
                return Results.BadRequest();
            }

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (form.Files.GetFile(FileField) is not { } file)
            {
                return Results.BadRequest();
            }

            await using var upload = file.OpenReadStream();
            var problem = await backups.StageRestoreAsync(upload, context.RequestAborted);
            return Results.LocalRedirect($"/status?{RestoreQuery}={problem?.ToString() ?? Staged}");
        });
    }
}
