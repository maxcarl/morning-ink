using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// Karakeep through its REST API: check the key, read the bookmarks not archived yet, archive them. Every connection
/// passes the home-network check, and nothing follows a redirect, so the API key goes to the address the user entered
/// and nowhere else.
/// </summary>
public sealed class KarakeepClient(IHttpClientFactory httpClients)
{
    public const string HttpClientName = "karakeep";

    private const int MaxItems = 100;

    /// <summary>Null when the key works at <paramref name="instance"/>; otherwise why not.</summary>
    public async Task<SourceResult.Failure?> CheckAsync(Uri instance, string apiKey, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, instance, "users/me", apiKey);
        var (body, failure) = await SendAsync(request, instance, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        // Any web server answers something; only Karakeep answers with the user the key belongs to.
        try
        {
            using var json = JsonDocument.Parse(body!); // ! is safe: a call without failure has a body
            return Json.Text(json.RootElement, "id") is { Length: > 0 } ? null : NotKarakeep(instance);
        }
        catch (JsonException)
        {
            return NotKarakeep(instance);
        }
    }

    private static SourceResult.Failure NotKarakeep(Uri instance) =>
        new($"There is no Karakeep at {instance.Host}.", Reason: "KarakeepMissing", Arguments: [instance.Host]);

    /// <summary>The bookmarks of web pages that are not archived, with the text Karakeep saved of them.</summary>
    public async Task<SourceResult> UnarchivedAsync(Uri instance, string apiKey, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, instance, string.Create(CultureInfo.InvariantCulture, $"bookmarks?archived=false&includeContent=true&limit={MaxItems}"), apiKey);
        var (body, failure) = await SendAsync(request, instance, cancellationToken);
        if (failure is not null)
        {
            return failure;
        }

        try
        {
            using var json = JsonDocument.Parse(body!); // ! is safe: a call without failure has a body
            if (!json.RootElement.TryGetProperty("bookmarks", out var bookmarks) || bookmarks.ValueKind != JsonValueKind.Array)
            {
                return NoList;
            }

            return new SourceResult.Success([.. bookmarks.EnumerateArray().Select(ToArticle).OfType<FetchedArticle>()], ETag: null, LastModified: null);
        }
        catch (JsonException)
        {
            return NoList;
        }
    }

    /// <summary>Archives the bookmarks; the number archived, and the first problem in English, or null.</summary>
    public async Task<(int Archived, string? Problem)> ArchiveAsync(Uri instance, string apiKey, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var archived = 0;
        foreach (var id in ids)
        {
            using var request = Request(HttpMethod.Patch, instance, $"bookmarks/{Uri.EscapeDataString(id)}", apiKey);
            request.Content = new StringContent("""{"archived":true}""", Encoding.UTF8, "application/json");
            if ((await SendAsync(request, instance, cancellationToken)).Failure is { } failure)
            {
                return (archived, failure.Message);
            }

            archived++;
        }

        return (archived, null);
    }

    private static readonly SourceResult.Failure NoList = new("Karakeep returned no readable list of bookmarks.", Reason: "KarakeepNoList");

    private static HttpRequestMessage Request(HttpMethod method, Uri instance, string path, string apiKey)
    {
        var request = new HttpRequestMessage(method, new Uri($"{instance.AbsoluteUri.TrimEnd('/')}/api/v1/{path}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    // Expected problems become the same reason codes as any fetch, so the page explains them in the user's language.
    private async Task<(string? Body, SourceResult.Failure? Failure)> SendAsync(HttpRequestMessage request, Uri instance, CancellationToken cancellationToken)
    {
        var host = instance.Host;
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is 401 or 403)
            {
                return (null, new SourceResult.Failure($"Karakeep at {host} refused the API key.", Reason: "KarakeepLogin"));
            }

            if (!response.IsSuccessStatusCode)
            {
                var code = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                return (null, new SourceResult.Failure($"{host} answered with HTTP {code}.", Reason: nameof(FetchError.HttpError), Arguments: [host, code]));
            }

            return (await response.Content.ReadAsStringAsync(cancellationToken), null);
        }
        catch (HttpRequestException e) when (e.InnerException is BlockedAddressException blocked)
        {
            return (null, new SourceResult.Failure(blocked.Message, blocked.AllowlistEntry, nameof(FetchError.BlockedAddress), [blocked.AllowlistEntry]));
        }
        catch (HttpRequestException)
        {
            return (null, new SourceResult.Failure($"{host} could not be reached.", Reason: nameof(FetchError.Unreachable), Arguments: [host]));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, new SourceResult.Failure($"{host} did not answer in time.", Reason: nameof(FetchError.Timeout), Arguments: [host]));
        }
    }

    // Bookmarks are untrusted: only saved web pages with a web link, never notes, "javascript:" or "file:".
    private static FetchedArticle? ToArticle(JsonElement bookmark)
    {
        if (!bookmark.TryGetProperty("content", out var content) || Json.Text(content, "type") != "link"
            || !Uri.TryCreate(Json.Text(content, "url"), UriKind.Absolute, out var url) || UrlPolicy.Check(url) is not null)
        {
            return null;
        }

        // The title the user gave it first, then the page's own.
        var title = (Json.Text(bookmark, "title") is { Length: > 0 } own ? own : Json.Text(content, "title"))?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return null;
        }

        // The text Karakeep saved is the article; only without it is the page fetched.
        var saved = Json.Text(content, "htmlContent") is { Length: > 0 } html ? html : null;
        return new FetchedArticle(
            Id: Json.Text(bookmark, "id"),
            Title: title,
            Url: url,
            PublishedAt: DateTimeOffset.TryParse(Json.Text(bookmark, "createdAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null,
            Author: Json.Text(content, "author"),
            Summary: Json.Text(content, "description"),
            Content: saved)
        {
            IsComplete = saved is not null,
        };
    }
}
