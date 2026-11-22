using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MorningInk.Infrastructure.Delivery;

namespace MorningInk.Infrastructure.Tests.Delivery;

public class CertificateCheckTests
{
    [Fact]
    public void Clean_certificate_is_trusted() =>
        Assert.True(CertificateCheck.IsTrusted(SslPolicyErrors.None, []));

    [Theory]
    [InlineData(X509ChainStatusFlags.RevocationStatusUnknown)] // e.g. Gmail: no revocation service for its short-lived certificates
    [InlineData(X509ChainStatusFlags.OfflineRevocation)]
    public void Revocation_that_cannot_be_checked_does_not_block(X509ChainStatusFlags status) =>
        Assert.True(CertificateCheck.IsTrusted(SslPolicyErrors.RemoteCertificateChainErrors, [status]));

    [Theory]
    [InlineData(X509ChainStatusFlags.UntrustedRoot)]
    [InlineData(X509ChainStatusFlags.Revoked)]
    [InlineData(X509ChainStatusFlags.NotTimeValid)]
    [InlineData(X509ChainStatusFlags.PartialChain)]
    public void Real_chain_problems_still_block(X509ChainStatusFlags status) =>
        Assert.False(CertificateCheck.IsTrusted(SslPolicyErrors.RemoteCertificateChainErrors, [status, X509ChainStatusFlags.RevocationStatusUnknown]));

    [Fact]
    public void Wrong_name_blocks_even_without_chain_problems() =>
        Assert.False(CertificateCheck.IsTrusted(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, [X509ChainStatusFlags.RevocationStatusUnknown]));
}
