using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace MorningInk.Infrastructure.Delivery;

/// <summary>Decides whether a mail server's certificate is good enough: trusted chain and matching name.</summary>
/// <remarks>
/// A revocation status that cannot be found out does not block, as in browsers ("soft fail"). Some providers no longer
/// run a revocation service for short-lived certificates (Gmail, 2026), so a strict check would refuse them everywhere.
/// A certificate that is known to be revoked still blocks.
/// </remarks>
internal static class CertificateCheck
{
    private static readonly X509ChainStatusFlags Undecided =
        X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

    public static bool IsTrusted(SslPolicyErrors errors, IEnumerable<X509ChainStatusFlags> chainStatus)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // Only chain errors can be forgiven, and only if each of them is an undecided revocation check.
        return errors == SslPolicyErrors.RemoteCertificateChainErrors
            && chainStatus.All(status => (status & ~Undecided) == X509ChainStatusFlags.NoError);
    }

    public static bool IsTrusted(SslPolicyErrors errors, X509Chain? chain) =>
        IsTrusted(errors, chain?.ChainElements.SelectMany(e => e.ChainElementStatus).Select(s => s.Status) ?? []);
}
