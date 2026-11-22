using System.Globalization;
using Microsoft.Extensions.Localization;

namespace MorningInk.Web;

/// <summary>Times as people say them in the UI language: "06:00 Uhr" in German, "6:00 AM" in English.</summary>
public static class Clock
{
    public static string Time(IStringLocalizer<SharedResource> texts, TimeOnly time) =>
        texts["Clock_Time", time.ToString("t", CultureInfo.CurrentCulture)];

    public static string Time(IStringLocalizer<SharedResource> texts, DateTimeOffset at) =>
        texts["Clock_Time", at.ToString("t", CultureInfo.CurrentCulture)];

    public static string DateAndTime(IStringLocalizer<SharedResource> texts, DateTimeOffset at) =>
        texts["Clock_DateTime", at.ToString("d", CultureInfo.CurrentCulture), Time(texts, at)];
}
