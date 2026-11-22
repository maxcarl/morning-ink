using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace MorningInk.Infrastructure.Http;

/// <summary>
/// The only way Morning Ink fetches external URLs. It enforces the URL rules, blocks internal addresses
/// (see <see cref="SafeConnector"/>), follows at most a few redirects and limits time and size.
/// </summary>
public sealed class SafeHttpClient
{
    private const int ReadBufferSize = 81920;

    private readonly HttpClient _http;
    private readonly SafeHttpOptions _options;
    private readonly TimeProvider _time;

    public SafeHttpClient(HttpClient http, IOptions<SafeHttpOptions> options, TimeProvider time)
    {
        _http = http;
        _options = options.Value;
        _time = time;
    }

    /// <summary>Fetches <paramref name="uri"/> with GET.</summary>
    /// <param name="uri">Untrusted address, e.g. from a feed or user input.</param>
    /// <param name="authorization">Sent only to the original host; dropped once a redirect leaves it.</param>
    /// <param name="validators">From an earlier fetch; an unchanged resource then yields <see cref="FetchResult.NotModified"/>.</param>
    /// <param name="maxResponseBytes">Lower size limit for this request, e.g. for images; never above the configured one.</param>
    /// <param name="cancellationToken">Cancels the request; cancellation is passed on, not reported as a timeout.</param>
    public async Task<FetchResult> GetAsync(
        Uri uri,
        AuthenticationHeaderValue? authorization = null,
        CacheValidators? validators = null,
        long? maxResponseBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (UrlPolicy.Check(uri) is { } invalid)
        {
            return new FetchResult.Failure(FetchError.InvalidUrl, invalid);
        }

        // One deadline for all redirects and the body, so a chain of slow hops cannot add up.
        using var timeout = new CancellationTokenSource(_options.Timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var limit = Math.Min(maxResponseBytes ?? long.MaxValue, _options.MaxResponseBytes);
            return await FollowAsync(uri, authorization, validators, limit, linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new FetchResult.Failure(FetchError.Timeout, $"{uri.Host} did not answer in time.", Arguments: [uri.Host]);
        }
        catch (HttpRequestException e) when (e.InnerException is BlockedAddressException blocked)
        {
            return new FetchResult.Failure(FetchError.BlockedAddress, blocked.Message, blocked.AllowlistEntry, [blocked.AllowlistEntry]);
        }
        catch (HttpRequestException)
        {
            return new FetchResult.Failure(FetchError.Unreachable, $"{uri.Host} could not be reached.", Arguments: [uri.Host]);
        }
    }

    private async Task<FetchResult> FollowAsync(
        Uri uri,
        AuthenticationHeaderValue? authorization,
        CacheValidators? validators,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var current = uri;
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Authorization = authorization;
            AddValidators(request, validators);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new FetchResult.NotModified();
            }

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
            {
                return response.IsSuccessStatusCode
                    ? await ReadBodyAsync(response, current, maxBytes, cancellationToken)
                    : new FetchResult.Failure(
                        FetchError.HttpError,
                        $"{current.Host} answered with HTTP {(int)response.StatusCode}.",
                        Arguments: [current.Host, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)]);
            }

            if (redirects == _options.MaxRedirects)
            {
                return new FetchResult.Failure(
                    FetchError.TooManyRedirects,
                    $"More than {_options.MaxRedirects} redirects.",
                    Arguments: [_options.MaxRedirects.ToString(CultureInfo.InvariantCulture)]);
            }

            var next = new Uri(current, location);
            if (UrlPolicy.Check(next) is { } invalid)
            {
                return new FetchResult.Failure(FetchError.InvalidUrl, $"Redirect rejected: {invalid}");
            }

            // Credentials stay with the host they were given for, and never travel over a downgrade to http.
            if (!string.Equals(next.Host, current.Host, StringComparison.OrdinalIgnoreCase)
                || (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp))
            {
                authorization = null;
            }

            current = next;
        }
    }

    private static async Task<FetchResult> ReadBodyAsync(HttpResponseMessage response, Uri finalUri, long maxBytes, CancellationToken cancellationToken)
    {
        // In whole MiB for people; the English message keeps the exact limit for logs.
        var megabytes = Math.Max(1, maxBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);
        var tooLarge = new FetchResult.Failure(FetchError.TooLarge, $"The response is larger than {maxBytes} bytes.", Arguments: [megabytes]);
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            return tooLarge;
        }

        // Content-Length can be missing or wrong, and compressed bodies grow; count what is actually read.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[ReadBufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + read > maxBytes)
            {
                return tooLarge;
            }

            body.Write(buffer, 0, read);
        }

        var received = new CacheValidators(response.Headers.ETag?.ToString(), response.Content.Headers.LastModified);
        return new FetchResult.Success(body.ToArray(), response.Content.Headers.ContentType?.MediaType, finalUri, received);
    }

    private static void AddValidators(HttpRequestMessage request, CacheValidators? validators)
    {
        // A malformed stored ETag is skipped rather than failing the fetch; the server then sends the full body.
        if (validators?.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var parsed))
        {
            request.Headers.IfNoneMatch.Add(parsed);
        }

        request.Headers.IfModifiedSince = validators?.LastModified;
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
