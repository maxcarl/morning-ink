using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MorningInk.Infrastructure.Tests.Delivery;

/// <summary>A server on localhost that only does a TLS handshake with a self-signed certificate, like a home-made mail server.</summary>
public sealed class SelfSignedTlsServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly X509Certificate2 _certificate;

    public SelfSignedTlsServer()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=mail.example.com", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Exported and loaded again: on some platforms SslStream needs a certificate whose key is not ephemeral.
        _certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Dispose()
    {
        _listener.Stop();
        _certificate.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync();
            await using var tls = new SslStream(client.GetStream());
            await tls.AuthenticateAsServerAsync(_certificate);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException or AuthenticationException)
        {
            // The client refuses the certificate: that is what the tests want.
        }
    }
}
