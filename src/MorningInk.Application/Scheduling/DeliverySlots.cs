namespace MorningInk.Application.Scheduling;

/// <summary>Computes the slots of a daily schedule, e.g. "6:00 in Europe/Berlin", in UTC.</summary>
public static class DeliverySlots
{
    /// <summary>The latest slot at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset LastAtOrBefore(DateTimeOffset now, TimeOnly time, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var slot = ToUtc(today, time, zone);
        return slot <= now ? slot : ToUtc(today.AddDays(-1), time, zone);
    }

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
