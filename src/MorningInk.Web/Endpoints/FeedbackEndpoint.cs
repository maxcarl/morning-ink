using System.Net;
using System.Text;
using MorningInk.Infrastructure.Feedback;

namespace MorningInk.Web;

/// <summary>
/// "More of this" and "less of this" from the e-reader's browser, which has no sign-in: the signed link is the
/// reader's right to answer. The answer is a tiny page of its own, nothing of the web UI.
/// </summary>
public static class FeedbackEndpoint
{
    public static void MapFeedback(this WebApplication app) =>
        app.MapGet("/feedback/{token}", async (string token, HttpContext context, FeedbackRecorder recorder) =>
        {
            var outcome = await recorder.RecordAsync(token, context.RequestAborted);

            // A body of its own either way, so the status code pages do not swap it for the "not found" page.
            var page = $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>Morning Ink</title></head><body><p>{WebUtility.HtmlEncode(outcome.Message)}</p></body></html>";
            return Results.Content(page, "text/html", Encoding.UTF8, outcome.Kind is null ? StatusCodes.Status400BadRequest : StatusCodes.Status200OK);
        }).AllowAnonymous();
}
