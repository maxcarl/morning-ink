using System.Net.Security;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.IO;
using MorningInk.Application.Delivery;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Delivery;

/// <summary>Sends an issue as an email attachment, e.g. to a Send-to-Kindle address.</summary>
internal sealed partial class MailDelivery(MailSettingsStore settings, ILogger<MailDelivery> logger) : IDelivery
{
    // Stricter than MimeKit's default, which accepts addresses without a domain.
    private static readonly ParserOptions StrictAddresses = new() { AllowAddressesWithoutDomain = false };

    public async Task<DeliveryResult> DeliverAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        var smtp = await settings.EffectiveAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(smtp.Host) || !TryParseAddress(smtp.FromAddress, out var from))
        {
            return new DeliveryResult.Failure("SMTP is not configured: server and sender address are needed.", Retryable: false, "NoMailServer");
        }

        if (!TryParseAddress(request.Recipient, out var to))
        {
            return new DeliveryResult.Failure("The recipient is not a single valid email address.", Retryable: false, "InvalidRecipient");
        }

        await using var file = File.OpenRead(request.FilePath);
        using var message = BuildMessage(new MailboxAddress(smtp.FromName, from.Address), to, request, file);

        // Measured as sent, Base64 included (about a third more than the file), before connecting at all.
        var size = Measure(message);
        if (size > smtp.MaxMessageBytes)
        {
            var (sizeMb, limitMb) = (size / (1024 * 1024), smtp.MaxMessageBytes / (1024 * 1024));
            return new DeliveryResult.Failure($"The issue is {sizeMb} MB as email; the limit is {limitMb} MB.", Retryable: false, "TooLarge", [Number(sizeMb), Number(limitMb)]);
        }

        file.Position = 0;
        return await SendAsync(smtp, smtp.Host, message, size, cancellationToken);
    }

    // The host is passed separately because it was checked for null above.
    private async Task<DeliveryResult> SendAsync(SmtpOptions smtp, string host, MimeMessage message, long size, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = (int)smtp.Timeout.TotalMilliseconds };

        // Trusted chain and matching name (see CertificateCheck); the outcome is kept to explain a failed handshake.
        SslPolicyErrors? certificateErrors = null;
        client.ServerCertificateValidationCallback = (_, _, chain, errors) =>
        {
            var trusted = CertificateCheck.IsTrusted(errors, chain);
            certificateErrors = trusted ? SslPolicyErrors.None : errors;
            return trusted;
        };
        try
        {
            await client.ConnectAsync(host, smtp.Port, ToSocketOptions(smtp.Security), cancellationToken);
            if (!string.IsNullOrEmpty(smtp.Username))
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
            LogDelivered(logger, size);
            return new DeliveryResult.Delivered();
        }
        catch (SmtpCommandException e)
        {
            // 4xx means "try again later", 5xx means "never".
            var retryable = (int)e.StatusCode is >= 400 and < 500;
            LogFailed(logger, (int)e.StatusCode);
            return new DeliveryResult.Failure($"The mail server refused the issue: {e.Message}", retryable, "Refused", [e.Message]);
        }
        catch (AuthenticationException)
        {
            return new DeliveryResult.Failure("The mail server rejected the login. Check user name and password.", Retryable: false, "LoginRejected");
        }
        catch (SslHandshakeException)
        {
            return HandshakeProblem(host, smtp.Port, certificateErrors);
        }
        catch (Exception e) when (e is SocketException or IOException or TimeoutException or ServiceNotConnectedException or SmtpProtocolException)
        {
            return new DeliveryResult.Failure($"The mail server {host} could not be reached.", Retryable: true, "Unreachable", [host]);
        }
    }

    private static MimeMessage BuildMessage(MailboxAddress from, MailboxAddress to, DeliveryRequest request, Stream file)
    {
        var title = HeaderText.SingleLine(request.Title);
        var attachment = new MimePart(ContentType.Parse(request.MediaType))
        {
            Content = new MimeContent(file),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,

            // Send to Kindle may use the file name as the book title, so it carries the issue title.
            FileName = HeaderText.FileName(request.Title, request.FileExtension),
        };

        var message = new MimeMessage { Subject = title };
        message.From.Add(from);
        message.To.Add(to);
        message.Body = new Multipart("mixed") { new TextPart("plain") { Text = title }, attachment };
        return message;
    }

    private static long Measure(MimeMessage message)
    {
        using var measuring = new MeasuringStream();
        message.WriteTo(measuring);
        return measuring.Length;
    }

    // Exactly one plain address: no lists, no line breaks, no address without a domain.
    private static bool TryParseAddress(string? text, out MailboxAddress address)
    {
        address = null!; // ! is safe: only read when this method returns true
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(['\r', '\n', ',', ';']) >= 0
            || !MailboxAddress.TryParse(StrictAddresses, text.Trim(), out var parsed) || !parsed.Address.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        address = parsed;
        return true;
    }

    // Three causes with three different fixes, so the message names the one that happened.
    private static DeliveryResult.Failure HandshakeProblem(string host, int port, SslPolicyErrors? certificateErrors) => certificateErrors switch
    {
        { } errors when errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors) => new(
            $"The mail server {host} presents a certificate that is not trusted (self-signed or from an unknown authority).", Retryable: false, "TlsUntrusted", [host]),
        { } errors when errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) => new(
            $"The certificate of the mail server is issued for another name than {host}. Enter the server name exactly as your mail provider gives it, not an IP address.", Retryable: false, "TlsName", [host]),

        // No certificate was checked at all: the two sides did not even agree to encrypt.
        _ => new(
            $"No encrypted connection to {host} on port {port}: the encryption setting probably does not fit the port. Port 587 usually uses STARTTLS, port 465 SSL/TLS.", Retryable: false, "TlsPort", [host, Number(port)]),
    };

    private static string Number(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static SecureSocketOptions ToSocketOptions(MailSecurity security) => security switch
    {
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        MailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.None,
    };

    // Neither recipient nor server reply is logged: both can contain the reader's address.
    [LoggerMessage(Level = LogLevel.Information, Message = "Delivered an issue by email ({Bytes} bytes)")]
    private static partial void LogDelivered(ILogger logger, long bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The mail server refused an issue with status {Status}")]
    private static partial void LogFailed(ILogger logger, int status);
}
