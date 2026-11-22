using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.Json;
using MorningInk.Application.Sources;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Sources;

/// <summary>A category of the user's FreshRSS: its stream id, e.g. "user/-/label/Technik", and its name.</summary>
public sealed record FreshRssCategory(string Stream, string Name);

/// <summary>
/// FreshRSS through its Google Reader API ("greader.php"): sign in, list categories, read unread articles, mark read.
/// Every connection passes the home-network check, and nothing follows a redirect, so the password and the session go
/// to the address the user entered and nowhere else.
/// </summary>
public sealed class FreshRssClient(IHttpClientFactory httpClients)
{
    public const string HttpClientName = "freshrss";

    private const string ReadState = "user/-/state/com.google/read";
    private const string LabelPrefix = "user/-/label/";
    private const int MaxItems = 100;

    /// <summary>Signs in with the API password (set in FreshRSS under Profile → API management); the answer is the session.</summary>
    public async Task<Answer> SignInAsync(Uri api, string user, string password, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(api, "accounts/ClientLogin"))
        {
            Content = new FormUrlEncodedContent([new("Email", user), new("Passwd", password)]),
        };
        var answer = await SendAsync(request, cancellationToken);
        if (!answer.Ok)
        {
            return answer.Failure.Arguments is [_, "401" or "403"] ? Answer.Fail(LoginFailed(api)) : answer;
        }

        // "SID=…\nLSID=…\nAuth=…": the Auth line is the session for all further requests.
        var session = answer.Body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Auth=", StringComparison.Ordinal))?[5..];
        return string.IsNullOrEmpty(session) ? Answer.Fail(LoginFailed(api)) : Answer.With(session);
    }

    public async Task<(IReadOnlyList<FreshRssCategory> Categories, SourceResult.Failure? Failure)> CategoriesAsync(Uri api, string session, CancellationToken cancellationToken)
    {
        var answer = await GetAsync(api, "reader/api/0/tag/list?output=json", session, cancellationToken);
        if (!answer.Ok)
        {
            return ([], answer.Failure);
        }

        using var json = Parse(answer.Body);
        var categories = json?.RootElement.TryGetProperty("tags", out var tags) == true && tags.ValueKind == JsonValueKind.Array
            ? tags.EnumerateArray()
                .Select(t => Text(t, "id"))
                .OfType<string>()
                .Where(id => id.StartsWith(LabelPrefix, StringComparison.Ordinal))
                .Select(id => new FreshRssCategory(id, id[LabelPrefix.Length..]))
                .ToList()
            : [];
        return (categories, null);
    }

    /// <summary>The unread articles of the stream, newest first; null as stream means all unread.</summary>
    public async Task<SourceResult> UnreadAsync(Uri api, string session, string? stream, CancellationToken cancellationToken)
    {
        var id = stream ?? "user/-/state/com.google/reading-list";
        var path = $"reader/api/0/stream/contents/{EscapePath(id)}?output=json&n={MaxItems}&xt={Uri.EscapeDataString(ReadState)}";
        var answer = await GetAsync(api, path, session, cancellationToken);
        if (!answer.Ok)
        {
            return answer.Failure;
        }

        using var json = Parse(answer.Body);
        if (json is null || !json.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return new SourceResult.Failure("FreshRSS returned no readable list of articles.", Reason: "FreshRssNoList");
        }

        var articles = items.EnumerateArray().Select(ToArticle).OfType<FetchedArticle>().ToList();
        return new SourceResult.Success(articles, ETag: null, LastModified: null);
    }

    /// <summary>Marks the articles read; returns a problem in English, or null.</summary>
    public async Task<string?> MarkReadAsync(Uri api, string session, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        // Writing needs a short-lived token on top of the session.
        var token = await GetAsync(api, "reader/api/0/token", session, cancellationToken);
        if (!token.Ok)
        {
            return token.Failure.Message;
        }

        List<KeyValuePair<string, string>> form = [new("a", ReadState), new("T", token.Body.Trim()), .. ids.Select(id => KeyValuePair.Create("i", id))];
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(api, "reader/api/0/edit-tag")) { Content = new FormUrlEncodedContent(form) };
        request.Headers.TryAddWithoutValidation("Authorization", $"GoogleLogin auth={session}");
        return (await SendAsync(request, cancellationToken)).Failure?.Message;
    }

    private async Task<Answer> GetAsync(Uri api, string path, string session, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint(api, path));
        request.Headers.TryAddWithoutValidation("Authorization", $"GoogleLogin auth={session}");
        return await SendAsync(request, cancellationToken);
    }

    // Expected problems become the same reason codes as any fetch, so the page explains them in the user's language.
    private async Task<Answer> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                return Answer.Fail(new SourceResult.Failure($"{host} answered with HTTP {code}.", Reason: nameof(FetchError.HttpError), Arguments: [host, code]));
            }

            return Answer.With(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (HttpRequestException e) when (e.InnerException is BlockedAddressException blocked)
        {
            return Answer.Fail(new SourceResult.Failure(blocked.Message, blocked.AllowlistEntry, nameof(FetchError.BlockedAddress), [blocked.AllowlistEntry]));
        }
        catch (HttpRequestException)
        {
            return Answer.Fail(new SourceResult.Failure($"{host} could not be reached.", Reason: nameof(FetchError.Unreachable), Arguments: [host]));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Answer.Fail(new SourceResult.Failure($"{host} did not answer in time.", Reason: nameof(FetchError.Timeout), Arguments: [host]));
        }
    }

    private static SourceResult.Failure LoginFailed(Uri api) =>
        new($"FreshRSS at {api.Host} refused the sign-in.", Reason: "FreshRssLogin");

    private static Uri Endpoint(Uri api, string path) => new($"{api.AbsoluteUri.TrimEnd('/')}/{path}");

    // Stream ids keep their slashes in the path, as FreshRSS expects; everything else is escaped.
    private static string EscapePath(string stream) => string.Join('/', stream.Split('/').Select(Uri.EscapeDataString));

    private static JsonDocument? Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static FetchedArticle? ToArticle(JsonElement item)
    {
        var title = Text(item, "title")?.Trim();
        var link = FirstHref(item, "canonical") ?? FirstHref(item, "alternate");

        // Articles are untrusted: only web links, never "javascript:" or "file:".
        if (string.IsNullOrEmpty(title) || !Uri.TryCreate(link, UriKind.Absolute, out var url) || UrlPolicy.Check(url) is not null)
        {
            return null;
        }

        var origin = item.TryGetProperty("origin", out var o) ? Text(o, "title") : null;
        var content = item.TryGetProperty("summary", out var summary) ? Text(summary, "content") : null;
        return new FetchedArticle(
            Id: Text(item, "id"),
            Title: title,
            Url: url,
            PublishedAt: item.TryGetProperty("published", out var published) && published.TryGetInt64(out var seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
            Author: Text(item, "author") is { Length: > 0 } author ? author : origin,
            Summary: null,
            Content: content);
    }

    private static string? FirstHref(JsonElement item, string name) =>
        item.TryGetProperty(name, out var links) && links.ValueKind == JsonValueKind.Array
            ? links.EnumerateArray().Select(l => Text(l, "href")).FirstOrDefault(h => !string.IsNullOrEmpty(h))
            : null;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>What FreshRSS answered: a body, or why there is none.</summary>
    public sealed class Answer
    {
        private Answer(string? body, SourceResult.Failure? failure) => (Body, Failure) = (body, failure);

        public string? Body { get; }

        public SourceResult.Failure? Failure { get; }

        [MemberNotNullWhen(true, nameof(Body))]
        [MemberNotNullWhen(false, nameof(Failure))]
        public bool Ok => Failure is null;

        public static Answer With(string body) => new(body, null);

        public static Answer Fail(SourceResult.Failure failure) => new(null, failure);
    }
}
