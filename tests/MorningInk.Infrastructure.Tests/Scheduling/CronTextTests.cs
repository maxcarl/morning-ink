using System.Globalization;
using MorningInk.Infrastructure.Scheduling;

namespace MorningInk.Infrastructure.Tests.Scheduling;

public class CronTextTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    // Saturday 2026-11-21, 7:00 in Berlin.
    private static readonly DateTimeOffset Saturday = DateTimeOffset.Parse("2026-11-21T06:00:00Z", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0 6 * * 1-5", "2026-11-23T06:00")]
    [InlineData("0 6 * * MON-FRI", "2026-11-23T06:00")]
    [InlineData("0 8 * * 0", "2026-11-22T08:00")]
    [InlineData("0 8 * * 7", "2026-11-22T08:00")]
    [InlineData("30 18 * * *", "2026-11-21T18:30")]
    [InlineData("0 6 1 * *", "2026-12-01T06:00")]
    [InlineData("0  6 * *  6 ", "2026-11-28T06:00")]
    [InlineData("15 */2 * * *", "2026-11-21T08:15")]
    [InlineData("@daily", "2026-11-22T00:00")]
    public void Crontab_expression_fires_next_at_its_local_time(string text, string expected)
    {
        var cron = CronText.Parse(text, Berlin);

        Assert.NotNull(cron);
        var next = TimeZoneInfo.ConvertTime(cron.GetNextValidTimeAfter(Saturday)!.Value, Berlin); // ! is safe: every valid expression fires again
        Assert.Equal(expected, next.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Weekday_steps_count_from_sunday_as_in_crontab()
    {
        var cron = CronText.Parse("0 6 * * */2", Berlin);

        Assert.NotNull(cron);
        var days = new List<DayOfWeek>();
        var at = Saturday;
        for (var i = 0; i < 4; i++)
        {
            at = cron.GetNextValidTimeAfter(at)!.Value; // ! is safe: every valid expression fires again
            days.Add(TimeZoneInfo.ConvertTime(at, Berlin).DayOfWeek);
        }

        Assert.Equal([DayOfWeek.Sunday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday], days);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0 6 * *")]
    [InlineData("0 0 6 * * ?")]
    [InlineData("*/5 6 * * *")] // more often than once an hour
    [InlineData("0,30 6 * * *")]
    [InlineData("* 6 * * *")]
    [InlineData("60 6 * * *")]
    [InlineData("0 24 * * *")]
    [InlineData("0 6 30 2 *")] // never
    [InlineData("0 6 * * 8")]
    public void Invalid_or_too_frequent_expression_is_rejected(string? text) =>
        Assert.Null(CronText.Parse(text, Berlin));
}
