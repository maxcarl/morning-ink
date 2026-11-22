namespace MorningInk.Web.Components;

/// <summary>The time zones the UI offers.</summary>
public static class TimeZones
{
    /// <summary>IANA ids ("Europe/Berlin"), which every platform .NET runs on understands since .NET 6.</summary>
    public static IReadOnlyList<string> All { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out var iana) ? iana : z.Id)
        .Where(id => id.Contains('/', StringComparison.Ordinal))
        .Distinct()
        .Order(StringComparer.Ordinal)
        .ToList();
}
