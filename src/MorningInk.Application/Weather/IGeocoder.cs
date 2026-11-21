using MorningInk.Domain;

namespace MorningInk.Application.Weather;

/// <summary>Turns a postal code or town into a place with coordinates.</summary>
public interface IGeocoder
{
    /// <summary>Returns the place, or null if it is unknown or the service is unavailable.</summary>
    /// <param name="region">Postal code or town, e.g. "10115" or "Wien".</param>
    /// <param name="country">ISO country code to search in, e.g. "DE"; null searches worldwide, where postal codes are ambiguous.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<RegionLocation?> ResolveAsync(string region, string? country, CancellationToken cancellationToken);
}
