using MorningInk.Domain;

namespace MorningInk.Application.Weather;

/// <summary>Turns a postal code or town into a place with coordinates.</summary>
public interface IGeocoder
{
    /// <summary>Returns the place, or null if it is unknown or the service is unavailable.</summary>
    Task<RegionLocation?> ResolveAsync(string region, CancellationToken cancellationToken);
}
