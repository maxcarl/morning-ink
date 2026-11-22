using System.Globalization;
using Microsoft.Extensions.Localization;
using MorningInk.Application.Scheduling;
using MorningInk.Domain;

namespace MorningInk.Web;

/// <summary>How an issue reads in a list, the same on the overview and in the archive.</summary>
public static class IssueTexts
{
    /// <summary>"Delivered", or "Made available" for an edition the reader fetches itself.</summary>
    public static string Status(IStringLocalizer<SharedResource> texts, Issue issue, Profile? profile) =>
        issue.Status == IssueStatus.Delivered && profile is { SendByEmail: false } ? texts["Status_Stored"] : texts[$"Status_{issue.Status}"];

    public static string StatusClass(IssueStatus status) => status switch
    {
        IssueStatus.Delivered => "status-ok",
        IssueStatus.Failed => "status-error",
        IssueStatus.Empty => "muted",
        _ => "status-warn",
    };

    /// <summary>"12 articles", "1.4 MB": what every list shows of an issue.</summary>
    public static List<string> Facts(IStringLocalizer<SharedResource> texts, Issue issue)
    {
        var facts = new List<string>();

        // Merged duplicates are entries too, only so they count as seen. Issues from before entries were kept have
        // none, and "0 articles" would be wrong for them, so the count is left out.
        var articles = issue.Entries.Count(e => !e.Duplicate);
        if (articles > 0)
        {
            facts.Add(texts["Dash_Articles", articles]);
        }

        if (issue.FileSize is { } size)
        {
            facts.Add(string.Create(CultureInfo.CurrentCulture, $"{size / 1024.0 / 1024.0:0.0} MB"));
        }

        return facts;
    }

    /// <summary>A time in the edition's own time zone.</summary>
    public static DateTimeOffset Local(DateTimeOffset utc, Profile? profile) =>
        TimeZoneInfo.ConvertTime(utc, ProfileTimeZone.Find(profile?.TimeZoneId ?? "UTC"));
}
