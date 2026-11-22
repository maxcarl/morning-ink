using System.Globalization;
using MorningInk.Application.Scheduling;

namespace MorningInk.Application.Tests.Scheduling;

public class DeliverySlotsTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Theory]
    [InlineData("2026-11-21T07:00:00+01:00", "06:00", "2026-11-21T05:00:00Z")] // an hour after the slot
    [InlineData("2026-11-21T06:00:00+01:00", "06:00", "2026-11-21T05:00:00Z")] // exactly at the slot
    [InlineData("2026-11-21T05:59:00+01:00", "06:00", "2026-11-20T05:00:00Z")] // before today's slot: yesterday
    [InlineData("2026-07-01T07:00:00+02:00", "06:00", "2026-07-01T04:00:00Z")] // summer time
    [InlineData("2026-03-29T04:00:00+02:00", "02:30", "2026-03-29T01:30:00Z")] // 2:30 does not exist that night
    [InlineData("2026-11-21T00:30:00+01:00", "23:00", "2026-11-20T22:00:00Z")] // late slot, just after midnight
    public void Last_slot_is_found_in_the_profile_time_zone(string now, string time, string expected) =>
        Assert.Equal(
            DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture),
            DeliverySlots.LastAtOrBefore(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), TimeOnly.Parse(time, CultureInfo.InvariantCulture), Berlin));

    [Theory]
    [InlineData("2026-11-21T05:00:00+01:00", "06:00", "2026-11-21T05:00:00Z")] // before today's slot: today
    [InlineData("2026-11-21T06:00:00+01:00", "06:00", "2026-11-22T05:00:00Z")] // exactly at the slot: it is under way, so tomorrow
    [InlineData("2026-11-21T23:00:00+01:00", "06:00", "2026-11-22T05:00:00Z")] // in the evening: tomorrow
    [InlineData("2026-03-28T07:00:00+01:00", "06:00", "2026-03-29T04:00:00Z")] // the clocks go forward overnight
    public void Next_slot_is_found_in_the_profile_time_zone(string now, string time, string expected) =>
        Assert.Equal(
            DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture),
            DeliverySlots.NextAfter(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), TimeOnly.Parse(time, CultureInfo.InvariantCulture), Berlin));

    [Fact]
    public void Unknown_time_zone_falls_back_to_utc() =>
        Assert.Equal(TimeZoneInfo.Utc, ProfileTimeZone.Find("Mars/Olympus_Mons"));
}
