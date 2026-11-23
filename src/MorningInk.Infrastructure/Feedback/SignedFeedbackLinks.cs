using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MorningInk.Application.Rendering;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Feedback;

/// <summary>
/// The feedback links in an issue: the entry and the kind, signed and encrypted with the instance's keys and valid for
/// a while. Nothing in them can be read or changed without the keys, so a link alone is the reader's right to answer.
/// </summary>
/// <remarks>The expiry is checked against <see cref="TimeProvider"/>, not the protector's clock, so it can be tested.</remarks>
public sealed class SignedFeedbackLinks(IDataProtectionProvider protection, IOptions<WebOptions> options, TimeProvider time) : IFeedbackLinks
{
    /// <summary>As long as feedback counts for the order, so an issue read late can still be answered.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    private readonly IDataProtector _protector = protection.CreateProtector("MorningInk.Feedback.v1");

    public FeedbackLinks? LinksFor(Guid entryId) =>
        options.Value.PublicUrl is { } site
            ? new FeedbackLinks(Link(site, entryId, FeedbackKind.More), Link(site, entryId, FeedbackKind.Less))
            : null;

    /// <summary>The entry and kind a link stands for; null for a changed, foreign or expired one.</summary>
    public (Guid Entry, FeedbackKind Kind)? Read(string token)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(_protector.Unprotect(Base64Url.DecodeFromChars(token))).Split('|');
            return parts is [var entry, var kind, var expires]
                && Guid.TryParseExact(entry, "N", out var entryId)
                && Enum.TryParse<FeedbackKind>(kind, out var feedback)
                && long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                && time.GetUtcNow() < DateTimeOffset.FromUnixTimeSeconds(seconds)
                ? (entryId, feedback)
                : null;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private Uri Link(Uri site, Guid entryId, FeedbackKind kind)
    {
        var expires = (time.GetUtcNow() + Lifetime).ToUnixTimeSeconds();
        var token = Base64Url.EncodeToString(_protector.Protect(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{entryId:N}|{kind}|{expires}"))));
        return new Uri($"{site.AbsoluteUri.TrimEnd('/')}/feedback/{token}");
    }
}
