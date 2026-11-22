using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using MorningInk.Application.Delivery;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Delivery;

public sealed class MailDeliveryTests : IDisposable
{
    private const string Kindle = "max_abc@kindle.com";
    private const string Title = "Morning Ink 2026-11-21";

    private readonly string _directory = Directory.CreateTempSubdirectory("morning-ink-").FullName;
    private readonly TestHost _host = new();

    [Fact]
    public async Task Issue_arrives_as_epub_attachment_named_like_the_issue()
    {
        using var server = new FakeSmtpServer();
        var file = WriteFile(1024);

        var result = await DeliverAsync(server, new DeliveryRequest(Kindle, Title, file, ".epub", "application/epub+zip"));

        Assert.IsType<DeliveryResult.Delivered>(result);
        var message = Assert.Single(server.Messages);
        Assert.Equal(Title, message.Subject);
        Assert.Equal(Kindle, message.To.Mailboxes.Single().Address);
        Assert.Equal("morning-ink@example.com", message.From.Mailboxes.Single().Address);
        var attachment = Assert.IsType<MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("Morning Ink 2026-11-21.epub", attachment.FileName);
        Assert.Equal("application/epub+zip", attachment.ContentType.MimeType);
        using var content = new MemoryStream();
        await attachment.Content!.DecodeToAsync(content); // ! is safe: MailDelivery always sets the content
        Assert.Equal(await File.ReadAllBytesAsync(file), content.ToArray());
    }

    [Fact]
    public async Task Line_breaks_cannot_inject_headers()
    {
        using var server = new FakeSmtpServer();

        await DeliverAsync(server, new DeliveryRequest(Kindle, "News\r\nBcc: attacker@example.com", WriteFile(10), ".epub", "application/epub+zip"));

        var message = Assert.Single(server.Messages);
        Assert.Empty(message.Bcc);
        Assert.DoesNotContain(message.Headers, h => h.Field.Equals("Bcc", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("News Bcc: attacker@example.com", message.Subject);
    }

    [Fact]
    public async Task File_name_cannot_contain_paths_or_line_breaks()
    {
        using var server = new FakeSmtpServer();

        await DeliverAsync(server, new DeliveryRequest(Kindle, "../../etc/passwd\r\nX: y", WriteFile(10), ".epub", "application/epub+zip"));

        var name = Assert.IsType<MimePart>(Assert.Single(Assert.Single(server.Messages).Attachments)).FileName;
        Assert.NotNull(name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\n', name);
        Assert.EndsWith(".epub", name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Too_large_issues_are_not_sent_at_all()
    {
        using var server = new FakeSmtpServer();

        var result = await DeliverAsync(server, new DeliveryRequest(Kindle, Title, WriteFile(800 * 1024), ".epub", "application/epub+zip"), maxMessageBytes: 1024 * 1024);

        var failure = Assert.IsType<DeliveryResult.Failure>(result);
        Assert.False(failure.Retryable);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task Login_uses_the_configured_credentials()
    {
        using var server = new FakeSmtpServer();

        await DeliverAsync(server, new DeliveryRequest(Kindle, Title, WriteFile(10), ".epub", "application/epub+zip"), username: "max", password: "geheim");

        Assert.Equal("max\0geheim", Assert.Single(server.Logins));
    }

    [Fact]
    public async Task Rejected_recipient_is_a_permanent_failure()
    {
        using var server = new FakeSmtpServer(Kindle);

        var result = await DeliverAsync(server, new DeliveryRequest(Kindle, Title, WriteFile(10), ".epub", "application/epub+zip"));

        Assert.False(Assert.IsType<DeliveryResult.Failure>(result).Retryable);
    }

    [Fact]
    public async Task Unreachable_server_is_worth_retrying()
    {
        int port;
        using (var server = new FakeSmtpServer())
        {
            port = server.Port;
        }

        var result = await DeliverAsync(port, new DeliveryRequest(Kindle, Title, WriteFile(10), ".epub", "application/epub+zip"));

        Assert.True(Assert.IsType<DeliveryResult.Failure>(result).Retryable);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("a@example.com\r\nBcc: c@example.com")]
    public async Task Invalid_recipients_are_rejected_before_sending(string recipient)
    {
        using var server = new FakeSmtpServer();

        var result = await DeliverAsync(server, new DeliveryRequest(recipient, Title, WriteFile(10), ".epub", "application/epub+zip"));

        Assert.False(Assert.IsType<DeliveryResult.Failure>(result).Retryable);
        Assert.Equal(0, server.Connections);
    }

    [Fact]
    public async Task Missing_smtp_settings_are_reported()
    {
        var result = await (await CreateDeliveryAsync(new Dictionary<string, string?>())).DeliverAsync(
            new DeliveryRequest(Kindle, Title, WriteFile(10), ".epub", "application/epub+zip"), CancellationToken.None);

        Assert.Contains("SMTP", Assert.IsType<DeliveryResult.Failure>(result).Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _host.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private string WriteFile(int size)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.epub");
        File.WriteAllBytes(path, Enumerable.Range(0, size).Select(i => (byte)i).ToArray());
        return path;
    }

    private Task<DeliveryResult> DeliverAsync(FakeSmtpServer server, DeliveryRequest request, long? maxMessageBytes = null, string? username = null, string? password = null) =>
        DeliverAsync(server.Port, request, maxMessageBytes, username, password);

    private async Task<DeliveryResult> DeliverAsync(int port, DeliveryRequest request, long? maxMessageBytes = null, string? username = null, string? password = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "127.0.0.1",
            ["Smtp:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Smtp:Security"] = "None",
            ["Smtp:FromAddress"] = "morning-ink@example.com",
            ["Smtp:Username"] = username,
            ["Smtp:Password"] = password,
        };
        if (maxMessageBytes is { } max)
        {
            settings["Smtp:MaxMessageBytes"] = max.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return await (await CreateDeliveryAsync(settings)).DeliverAsync(request, CancellationToken.None);
    }

    // Mail settings are read from the database too, so the delivery needs a migrated one.
    private async Task<IDelivery> CreateDeliveryAsync(Dictionary<string, string?> settings) =>
        (await _host.StartAsync(settings)).CreateScope().ServiceProvider.GetRequiredService<IDelivery>();
}
