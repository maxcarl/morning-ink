using System.Diagnostics.CodeAnalysis;
namespace MorningInk.Application.Scheduling;

/// <summary>Computes the slots of a daily schedule, e.g. "6:00 in Europe/Berlin", in UTC.</summary>
public static class DeliverySlots
{
    private const int DaysPerWeek = 7;

    /// <summary>The latest slot at or before <paramref name="now"/>.</summary>
    /// <param name="days">Weekdays with an issue, in the profile's time zone; null or empty means every day.</param>
    public static DateTimeOffset LastAtOrBefore(DateTimeOffset now, TimeOnly time, TimeZoneInfo zone, IReadOnlyCollection<DayOfWeek>? days = null)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        for (var back = 0; ; back++)
        {
            var date = today.AddDays(-back);
            var slot = ToUtc(date, time, zone);
            if (slot <= now && IsIssueDay(date, days))
            {
                return slot;
            }
        }
    }

    /// <summary>The first slot after <paramref name="now"/>; a slot exactly at <paramref name="now"/> counts as under way.</summary>
    /// <param name="days">Weekdays with an issue, in the profile's time zone; null or empty means every day.</param>
    public static DateTimeOffset NextAfter(DateTimeOffset now, TimeOnly time, TimeZoneInfo zone, IReadOnlyCollection<DayOfWeek>? days = null)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        for (var ahead = 0; ; ahead++)
        {
            var date = today.AddDays(ahead);
            var slot = ToUtc(date, time, zone);
            if (slot > now && IsIssueDay(date, days))
            {
                return slot;
            }
        }
    }

    /// <summary>Whether the days stand for "every day": none chosen, or all seven.</summary>
    public static bool IsEveryDay([NotNullWhen(false)] IReadOnlyCollection<DayOfWeek>? days) => days is null || days.Count == 0 || days.Distinct().Count() == DaysPerWeek;

    private static bool IsIssueDay(DateOnly date, IReadOnlyCollection<DayOfWeek>? days) => IsEveryDay(days) || days.Contains(date.DayOfWeek);

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        // On the night the clocks go forward, times like 2:30 do not exist; the issue then comes an hour later.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
