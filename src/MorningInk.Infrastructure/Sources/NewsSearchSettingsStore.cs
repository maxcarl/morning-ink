using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Sources;

/// <summary>
/// Which news search local sources use: one's own SearXNG from the settings page, or Bing. The environment variable
/// <c>NewsSearch__SearxngUrl</c> wins and makes the field read-only, like the mail settings.
/// </summary>
public sealed class NewsSearchSettingsStore(MorningInkDbContext db, IOptions<NewsSearchOptions> options, HostAllowlist allowlist, SearxngNewsSearch searxng)
{
    private static readonly TimeSpan TestRange = TimeSpan.FromDays(7);

    /// <summary>Whether the address comes from the environment; the page then shows it read-only.</summary>
    public bool IsLocked => !string.IsNullOrWhiteSpace(options.Value.SearxngUrl);

    /// <summary>The SearXNG to use, or null for Bing.</summary>
    public async Task<string?> SearxngUrlAsync(CancellationToken cancellationToken) =>
        IsLocked
            ? options.Value.SearxngUrl
            : await db.NewsSearchSettings.AsNoTracking().Select(n => n.SearxngUrl).SingleOrDefaultAsync(cancellationToken);

    /// <summary>Searches the saved SearXNG once for the term, so the admin sees at once whether it answers with JSON.</summary>
    /// <returns>Null when no SearXNG is set.</returns>
    public async Task<SourceResult?> TestAsync(string term, CancellationToken cancellationToken)
    {
        if (await SearxngUrlAsync(cancellationToken) is not { } url)
        {
            return null;
        }

        return await searxng.SearchAsync(new NewsQuery([term], TestRange), url, cancellationToken);
    }

    /// <summary>Saves the address; empty goes back to Bing. False when it is no http(s) address.</summary>
    /// <param name="adminId">The admin saving; the host is allowed in the home network in their name.</param>
    public async Task<bool> SaveAsync(string? address, Guid adminId, CancellationToken cancellationToken)
    {
        Uri? url = null;
        if (!string.IsNullOrWhiteSpace(address)
            && (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out url) || UrlPolicy.Check(url) is not null || !string.IsNullOrEmpty(url.Query)))
        {
            return false;
        }

        var settings = await db.SettingsRowAsync<NewsSearchSettings>(cancellationToken);

        settings.SearxngUrl = url?.AbsoluteUri.TrimEnd('/');
        await db.SaveChangesAsync(cancellationToken);

        // One's own SearXNG usually runs in the home network, which is blocked otherwise.
        if (url is not null)
        {
            await allowlist.AllowServiceAsync(url, adminId, cancellationToken);
        }

        return true;
    }
}
