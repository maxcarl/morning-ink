namespace MorningInk.Infrastructure.Weather;

internal static class Urls
{
    /// <summary>Appends query parameters to a configured endpoint, which may already have some.</summary>
    public static Uri WithQuery(string endpoint, IEnumerable<(string Name, string Value)> parameters)
    {
        var query = string.Join('&', parameters.Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value)}"));
        return new Uri(endpoint + (endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query);
    }
}
