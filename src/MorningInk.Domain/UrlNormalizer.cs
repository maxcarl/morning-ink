namespace MorningInk.Domain;

/// <summary>
/// Turns an article URL into a key that is the same for all variants of one article: scheme, "www.", host case,
/// default port, fragment, trailing slash, tracking parameters and parameter order do not matter.
/// </summary>
public static class UrlNormalizer
{
    private const string WwwPrefix = "www.";
    private const string UtmPrefix = "utm_";

    // Parameters that only say where a click came from. Kept conservative: a wrongly removed parameter
    // would merge two different articles into one.
    private static readonly HashSet<string> TrackingParameters = new(
    [
        "fbclid", "gclid", "dclid", "msclkid", "yclid", "igshid", "mc_cid", "mc_eid", "_hsenc", "_hsmi",
        "wt_mc", "wt_zmc", "at_medium", "at_campaign", "ref_src", "xtor",
        "ns_campaign", "ns_mchannel", "ns_source", "ns_linkname", "ns_fee",
    ], StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the key, e.g. "heise.de/news/x.html?id=1"; it is not a URL and never fetched.</summary>
    public static string Normalize(Uri url)
    {
        // IdnHost gives the punycode form, so "bücher.de" and "xn--bcher-kva.de" match.
        var host = url.IdnHost.ToLowerInvariant();
        if (host.StartsWith(WwwPrefix, StringComparison.Ordinal))
        {
            host = host[WwwPrefix.Length..];
        }

        var port = url.IsDefaultPort ? string.Empty : $":{url.Port}";
        var path = url.AbsolutePath.Length > 1 ? url.AbsolutePath.TrimEnd('/') : url.AbsolutePath;
        var parameters = url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter => !IsTracking(parameter.Split('=', 2)[0]))
            .Order(StringComparer.Ordinal)
            .ToList();
        var query = parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters);

        return host + port + path + query;
    }

    private static bool IsTracking(string name) =>
        name.StartsWith(UtmPrefix, StringComparison.OrdinalIgnoreCase) || TrackingParameters.Contains(name);
}
