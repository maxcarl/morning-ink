using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using MorningInk.Application.Delivery;
using MorningInk.Application.Notifications;
using MorningInk.Domain;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Rendering;

namespace MorningInk.Infrastructure.Notifications;

/// <summary>
/// Sends a short notice by ntfy, Gotify, Telegram or e-mail. Every connection passes the home-network check, so a
/// self-hosted ntfy or Gotify at home needs its host allowed, like any other address there.
/// </summary>
internal sealed class Notifier(IHttpClientFactory httpClients, MailDelivery mail, IOptions<NotificationOptions> options) : INotifier
{
    public const string HttpClientName = "notify";

    // ntfy shows a newspaper icon for this tag.
    private static readonly string[] NtfyTags = ["newspaper"];

    public async Task<string?> NotifyAsync(NotificationTarget target, Notice notice, CancellationToken cancellationToken)
    {
        var (title, text) = Words(notice);
        try
        {
            return target.Channel switch
            {
                NotificationChannel.Ntfy => await NtfyAsync(target, title, text, cancellationToken),
                NotificationChannel.Gotify => await GotifyAsync(target, title, text, cancellationToken),
                NotificationChannel.Telegram => await TelegramAsync(target, title, text, cancellationToken),
                NotificationChannel.Email => await mail.SendTextAsync(target.Target, title, text, cancellationToken) is DeliveryResult.Failure failure ? failure.Message : null,
                _ => null,
            };
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            return e.Message;
        }
    }

    // In the issue's language, like the issue itself.
    private static (string Title, string Text) Words(Notice notice)
    {
        var texts = EpubTexts.For(notice.Language);
        var text = notice.IsTest ? texts.NoticeTest
            : notice.Problem is null ? texts.NoticeDelivered(notice.EditionTitle, notice.Number, notice.Articles)
            : texts.NoticeFailed(notice.EditionTitle, notice.Number, notice.Problem);
        return (notice.EditionTitle, text);
    }

    // JSON to the server root rather than plain text to the topic: titles may then hold umlauts.
    private async Task<string?> NtfyAsync(NotificationTarget target, string title, string text, CancellationToken cancellationToken)
    {
        var topicUrl = new Uri(target.Target);
        var topic = topicUrl.AbsolutePath.Trim('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(topicUrl, "/"))
        {
            Content = JsonContent.Create(new { topic, title, message = text, tags = NtfyTags }),
        };
        if (!string.IsNullOrEmpty(target.Secret))
        {
            request.Headers.Authorization = new("Bearer", target.Secret);
        }

        return await SendAsync(request, cancellationToken);
    }

    private async Task<string?> GotifyAsync(NotificationTarget target, string title, string text, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(target.Target.TrimEnd('/') + "/"), "message"))
        {
            Content = JsonContent.Create(new { title, message = text, priority = 5 }),
        };
        request.Headers.Add("X-Gotify-Key", target.Secret ?? string.Empty);
        return await SendAsync(request, cancellationToken);
    }

    private async Task<string?> TelegramAsync(NotificationTarget target, string title, string text, CancellationToken cancellationToken)
    {
        var api = options.Value.TelegramApiUrl.TrimEnd('/');
        // "chat/thread" writes into one topic of a forum group; the editor only saves a numeric thread.
        var parts = target.Target.Split('/', 2);
        object body = parts.Length == 2
            ? new { chat_id = parts[0], message_thread_id = long.Parse(parts[1], CultureInfo.InvariantCulture), text = $"{title}\n{text}" }
            : new { chat_id = target.Target, text = $"{title}\n{text}" };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{api}/bot{target.Secret}/sendMessage"))
        {
            Content = JsonContent.Create(body),
        };
        return await SendAsync(request, cancellationToken);
    }

    // The status only: the URL may hold a bot token and is never part of the answer.
    private async Task<string?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}";
    }
}
