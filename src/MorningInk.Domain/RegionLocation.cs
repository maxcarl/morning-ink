namespace MorningInk.Domain;

/// <summary>Where a profile's region is, as resolved by geocoding; reused until the region text changes.</summary>
/// <param name="Query">The region text this was resolved from, e.g. "10115" or "Aachen".</param>
/// <param name="Name">Town or city, e.g. "Berlin".</param>
/// <param name="District">County or district, e.g. "Städteregion Aachen"; null for cities without one.</param>
/// <param name="Latitude">In degrees, -90 to 90.</param>
/// <param name="Longitude">In degrees, -180 to 180.</param>
public sealed record RegionLocation(string Query, string Name, string? District, double Latitude, double Longitude);
