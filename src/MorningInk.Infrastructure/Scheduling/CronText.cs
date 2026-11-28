using System.Globalization;
using Quartz;

namespace MorningInk.Infrastructure.Scheduling;

/// <summary>
/// Reads a crontab expression with five fields ("minute hour day month weekday", e.g. "0 6 * * 1-5"), the form users
/// know from Linux and Home Assistant; Quartz turns it into its own form.
/// </summary>
public static class CronText
{
    private const int FieldCount = 5;
    private const int MaxMinute = 59;
    private const char Macro = '@';

    /// <summary>The expression in <paramref name="zone"/>, or null when it is not valid crontab, never fires, or fires more often than once an hour.</summary>
    public static CronExpression? Parse(string? text, TimeZoneInfo zone)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var fields = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // A single minute keeps issues at least an hour apart, so a typo cannot fill the reader's inbox; the macros
        // (@daily, @weekly, …) fire hourly at most.
        var hourlyAtMost = trimmed.StartsWith(Macro)
            || (fields.Length == FieldCount && int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minute) && minute <= MaxMinute);
        if (!hourlyAtMost || !CronExpression.TryParse(trimmed, CronFormat.Unix, out var parsed))
        {
            return null;
        }

        // Without a year field, an expression that fires once fires forever; one like "30 February" never does.
        var expression = parsed.WithTimeZone(zone);
        return expression.GetNextValidTimeAfter(DateTimeOffset.UnixEpoch) is null ? null : expression;
    }
}
