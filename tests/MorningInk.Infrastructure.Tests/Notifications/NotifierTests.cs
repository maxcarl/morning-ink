using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Notifications;
using MorningInk.Domain;
using MorningInk.Infrastructure.Tests.Delivery;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Notifications;

public sealed class NotifierTests : IDisposable
{
    private static readonly Notice Delivered = new("Morning Ink", 14, 23, null, "de");

    private readonly TestHost _host = new();
    private readonly List<(string Path, string? Header, string Body)> _requests = [];
    private readonly LocalServer _server;
    private readonly FakeSmtpServer _smtp = new();

    public NotifierTests() => _server = new LocalServer(async context =>
    {
        using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
        var header = context.Request.Headers["Authorization"] ?? context.Request.Headers["X-Gotify-Key"];
        var body = await reader.ReadToEndAsync();
        _requests.Add((context.Request.Url!.AbsolutePath, header, body)); // ! is safe: a received request has a URL

        // A topic named "broken" is refused, as a server would refuse a topic without permission.
        context.Response.StatusCode = body.Contains("\"broken\"", StringComparison.Ordinal) ? (int)HttpStatusCode.Forbidden : (int)HttpStatusCode.OK;
    });

    [Fact]
    public async Task Ntfy_gets_title_and_message_for_its_topic()
    {
        var error = await NotifyAsync(new NotificationTarget(NotificationChannel.Ntfy, $"{Base}/morgenzeitung", "tk_secret"), Delivered);

        Assert.Null(error);
        var (path, header, body) = Assert.Single(_requests);
        Assert.Equal("/", path); // JSON to the server root, so titles may have umlauts
        Assert.Equal("Bearer tk_secret", header);
        Assert.Contains("\"topic\":\"morgenzeitung\"", body, StringComparison.Ordinal);
        Assert.Contains("Morning Ink Nr. 14 ist da: 23 Artikel.", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gotify_gets_its_app_token()
    {
        await NotifyAsync(new NotificationTarget(NotificationChannel.Gotify, Base, "app-token"), Delivered);

        var (path, header, body) = Assert.Single(_requests);
        Assert.Equal(("/message", "app-token"), (path, header));
        Assert.Contains("\"title\":\"Morning Ink\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Telegram_bot_writes_to_the_chat()
    {
        await NotifyAsync(new NotificationTarget(NotificationChannel.Telegram, "123456", "bot-token"), Delivered);

        var (path, _, body) = Assert.Single(_requests);
        Assert.Equal("/botbot-token/sendMessage", path);
        Assert.Contains("\"chat_id\":\"123456\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Telegram_writes_into_a_forum_thread()
    {
        await NotifyAsync(new NotificationTarget(NotificationChannel.Telegram, "-1001234567890/42", "bot-token"), Delivered);

        var (_, _, body) = Assert.Single(_requests);
        Assert.Contains("\"chat_id\":\"-1001234567890\"", body, StringComparison.Ordinal);
        Assert.Contains("\"message_thread_id\":42", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Email_goes_through_the_mail_server()
    {
        await NotifyAsync(new NotificationTarget(NotificationChannel.Email, "leser@example.com", null), Delivered with { Problem = "Der Mailserver ist nicht erreichbar." });

        var message = Assert.Single(_smtp.Messages);
        Assert.Contains("konnte nicht zugestellt werden", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refused_notification_is_reported_not_thrown()
    {
        var error = await NotifyAsync(new NotificationTarget(NotificationChannel.Ntfy, $"{Base}/broken", null), Delivered);

        Assert.Contains("403", error, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _server.Dispose();
        _smtp.Dispose();
        _host.Dispose();
    }

    private string Base => $"http://127.0.0.1:{_server.Port}";

    private async Task<string?> NotifyAsync(NotificationTarget target, Notice notice)
    {
        await using var services = await _host.StartAsync(new()
        {
            ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}",
            ["Notifications:TelegramApiUrl"] = Base,
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = _smtp.Port.ToString(CultureInfo.InvariantCulture),
            ["Smtp:Security"] = "None",
            ["Smtp:FromAddress"] = "morning-ink@example.com",
        });
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotifier>().NotifyAsync(target, notice, CancellationToken.None);
    }
}
