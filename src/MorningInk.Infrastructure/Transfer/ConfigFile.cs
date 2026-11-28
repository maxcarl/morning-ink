using MorningInk.Domain;

namespace MorningInk.Infrastructure.Transfer;

// The file a user exports and imports on the status page: what they set up, never a secret. Lists may be missing or
// null in a hand-edited file; the import reads them as empty. Sections name their sources, so the file reads and
// diffs well in Git.

/// <param name="Format">Always <see cref="ConfigTransfer.FormatName"/>, so a wrong file is told apart at once.</param>
/// <param name="Instance">The admin's instance settings; null in a file of a user who is no admin.</param>
public sealed record ConfigFile(
    string Format,
    int Version,
    IReadOnlyList<SourceConfig>? Sources,
    IReadOnlyList<EditionConfig>? Editions,
    InstanceConfig? Instance = null);

/// <summary>A source; FreshRSS, Karakeep and newsletters also need their login, which is never in the file.</summary>
public sealed record SourceConfig(
    string Name,
    SourceKind Kind,
    string? Url = null,
    string? Place = null,
    string? Country = null,
    string? Stream = null,
    bool FetchFullText = true,
    int MaxArticles = 20,
    IReadOnlyList<string>? IncludeKeywords = null,
    IReadOnlyList<string>? ExcludeKeywords = null,
    string? ExcludeTitlePattern = null,
    int? MinWords = null,
    int? MaxWords = Source.DefaultMaxWords,
    bool SkipLiveTickers = false,
    bool SkipAds = true,
    bool SkipMedia = true,
    bool LoadImages = true,
    bool MarkReadAfterDelivery = true,
    bool LocalAiOnly = false,
    int? MinScore = null,
    string? Sender = null);

/// <param name="DeliveryTime">"HH:mm" in <paramref name="TimeZone"/>.</param>
/// <param name="Days">Weekdays with an issue; empty for every day.</param>
/// <param name="NotifyTarget">The ntfy topic, Gotify server, Telegram chat or address; its token is not in the file.</param>
/// <param name="Cron">A crontab expression that replaces <paramref name="DeliveryTime"/> and <paramref name="Days"/>; null for none.</param>
public sealed record EditionConfig(
    string Title,
    string Recipient,
    string DeliveryTime,
    string TimeZone,
    bool SendByEmail = true,
    int LeadMinutes = 0,
    IReadOnlyList<DayOfWeek>? Days = null,
    int MinArticles = 1,
    string? Region = null,
    string? Country = null,
    NotificationChannel Notify = NotificationChannel.None,
    string? NotifyTarget = null,
    IReadOnlyList<InterestConfig>? Interests = null,
    IReadOnlyList<SectionConfig>? Sections = null,
    string? Cron = null);

public sealed record InterestConfig(string Topic, IReadOnlyList<string>? Keywords, InterestWeight Weight);

/// <param name="Sources">Names of the file's sources that fill an articles section.</param>
public sealed record SectionConfig(
    string Name,
    SectionKind Kind = SectionKind.Articles,
    bool Enabled = true,
    int? MaxArticles = null,
    IReadOnlyList<string>? Sources = null,
    bool Anniversaries = true,
    bool FeaturedArticle = false);

/// <summary>What only the admin sets; passwords and keys stay out.</summary>
public sealed record InstanceConfig(MailConfig? Mail, AiConfig? Ai, string? SearxngUrl, IReadOnlyList<string>? AllowedHosts);

public sealed record MailConfig(string? Host, int Port, MailSecurity Security, string? Username, string? FromAddress, string? FromName);

/// <param name="SummaryPrompt">Own instructions; null where the default is used, so improvements to it still arrive.</param>
public sealed record AiConfig(
    bool Enabled,
    string? Endpoint,
    string? Model,
    bool IsLocal,
    bool Summaries,
    int? DailyLimit,
    bool RecognizeInterests,
    bool NeutralHeadlines,
    string? SummaryPrompt,
    string? OverviewPrompt,
    string? HeadlinesPrompt,
    string? InterestsPrompt);
