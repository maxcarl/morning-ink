namespace MorningInk.Application.Scheduling;

/// <summary>Looks up a profile's time zone; an unknown id falls back to UTC instead of failing the issue.</summary>
public static class ProfileTimeZone
{
    public static TimeZoneInfo Find(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
