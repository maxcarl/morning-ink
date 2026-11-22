namespace MorningInk.Domain;

/// <summary>Where a profile's region is, as resolved by geocoding; reused until the region text changes.</summary>
/// <param name="Query">What this was resolved from, see <see cref="KeyFor"/>; a change of region or country makes it stale.</param>
/// <param name="Name">Town or city, e.g. "Berlin".</param>
/// <param name="District">County or district, e.g. "Städteregion Aachen"; null for cities without one.</param>
/// <param name="Latitude">In degrees, -90 to 90.</param>
/// <param name="Longitude">In degrees, -180 to 180.</param>
public sealed record RegionLocation(string Query, string Name, string? District, double Latitude, double Longitude)
{
    /// <summary>Key for a region in a country, e.g. "10115 (DE)"; without country the region alone.</summary>
    public static string KeyFor(string region, string? country) => country is null ? region : $"{region} ({country})";
}
