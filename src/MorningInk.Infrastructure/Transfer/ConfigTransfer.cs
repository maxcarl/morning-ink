using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MorningInk.Application.Publishing;
using MorningInk.Domain;
using MorningInk.Infrastructure.Ai;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Http;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Scheduling;
using MorningInk.Infrastructure.Sources;
using Quartz;

namespace MorningInk.Infrastructure.Transfer;

/// <summary>Something the import refused or could not finish.</summary>
/// <param name="Item">The source, edition or setting it is about, as named in the file.</param>
/// <param name="Code">What is wrong: "Format", a field such as "TimeZone" or "Recipient", or a reason such as "InvalidUrl".</param>
public sealed record ConfigProblem(string Item, string Code);

/// <summary>How an import went.</summary>
/// <param name="Problems">Empty when the sources and editions were imported; otherwise nothing of them was changed.</param>
/// <param name="NeedLogin">Sources skipped since they need a login the user does not have here yet.</param>
/// <param name="NeedToken">Editions whose notification needs its token entered again; it is off until then.</param>
/// <param name="InstanceProblems">Instance settings an admin's file had that their store refused; the rest was saved.</param>
public sealed record ConfigImport(
    IReadOnlyList<ConfigProblem> Problems,
    IReadOnlyList<string> NeedLogin,
    IReadOnlyList<string> NeedToken,
    IReadOnlyList<ConfigProblem> InstanceProblems)
{
    public bool Imported => Problems.Count == 0;
}

/// <summary>
/// Export and import of a user's setup as JSON: sources, editions with sections and interests, and for an admin the
/// instance settings. No secrets go into the file. An import replaces the user's sources and editions with the file's,
/// matched by kind and name and by title, so a file kept in Git describes the setup as it should be; matched ones keep
/// their id, archive and logins.
/// </summary>
/// <remarks>
/// Everything goes through the same editors as the pages, so a file can set nothing a page could not. The file is
/// untrusted: sizes are limited, and the user's part is imported in one transaction, all or nothing.
/// </remarks>
public sealed class ConfigTransfer(
    MorningInkDbContext db,
    SourceCatalog catalog,
    ProfileEditor editions,
    ISchedulerFactory schedulers,
    IssueScheduler issueScheduler,
    MailSettingsStore mail,
    AiSettingsStore ai,
    NewsSearchSettingsStore newsSearch,
    HostAllowlist allowlist)
{
    /// <summary>What every file says it is.</summary>
    public const string FormatName = "morning-ink-config";

    /// <summary>Largest file accepted; a large setup is a few dozen kilobytes.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    private const int Version = 1;
    private const int MaxSources = 1000;
    private const int MaxEditions = 50;
    private const int MaxSections = 50;
    private const int MaxSectionArticles = 100;
    private const int MaxAllowedHosts = 100;
    private const int MaxHostLength = 200; // the column's length
    private const string TimeFormat = "HH:mm";
    private const string FileItem = "file";
    private const string NotifySecretField = "NotifySecret";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 16,
    };

    private static readonly DayOfWeek[] EveryDay = Enum.GetValues<DayOfWeek>();

    // Editions this import created, so a rollback can drop their plans.
    private readonly List<Guid> _created = [];

    /// <summary>The user's setup as JSON; with the instance settings when <paramref name="isAdmin"/>.</summary>
    public async Task<string> ExportAsync(Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var sources = await catalog.ListAsync(userId, cancellationToken);
        var profiles = await editions.ListAsync(userId, cancellationToken);
        var file = new ConfigFile(
            FormatName,
            Version,
            [.. sources.Select(Export)],
            [.. profiles.Select(Export)],
            isAdmin ? await ExportInstanceAsync(cancellationToken) : null);
        return JsonSerializer.Serialize(file, Json);
    }

    /// <summary>Replaces the user's sources and editions with the file's; an admin's file also sets the instance.</summary>
    public async Task<ConfigImport> ImportAsync(Guid userId, bool isAdmin, Stream upload, CancellationToken cancellationToken)
    {
        if (await ReadAsync(upload, cancellationToken) is not { } file)
        {
            return Refused(new ConfigProblem(FileItem, "Format"));
        }

        if (Check(file) is { } problem)
        {
            return Refused(problem);
        }

        var needLogin = new List<string>();
        var needToken = new List<string>();
        var problems = new List<ConfigProblem>();
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var sourceIds = await ImportSourcesAsync(userId, file.Sources ?? [], problems, needLogin, cancellationToken);
            if (problems.Count == 0)
            {
                await ImportEditionsAsync(userId, file.Editions ?? [], sourceIds, problems, needToken, cancellationToken);
            }

            if (problems.Count > 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                await RestoreSchedulesAsync(userId, cancellationToken);
                return new ConfigImport(problems, [], [], []);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // After the commit: the allowlist writes in a scope of its own, and SQLite takes one writer at a time.
        var instanceProblems = isAdmin && file.Instance is { } instance ? await ImportInstanceAsync(userId, instance, cancellationToken) : [];
        return new ConfigImport([], needLogin, needToken, instanceProblems);
    }

    // The editors planned the editions they saved before the rollback: the user's plans go back to what the database
    // holds, without catching up a missed slot, and editions created in between lose theirs.
    private async Task RestoreSchedulesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var scheduler = await schedulers.GetScheduler(cancellationToken);
        var schedules = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new ProfileSchedule(p.Id, p.DeliveryTime, p.TimeZoneId, p.Days, p.LeadTime))
            .ToListAsync(cancellationToken);
        foreach (var schedule in schedules)
        {
            await issueScheduler.ScheduleAsync(scheduler, schedule, cancellationToken, catchUp: false);
        }

        foreach (var created in _created.Where(id => schedules.All(s => s.ProfileId != id)))
        {
            await scheduler.DeleteJob(IssueScheduler.JobKeyFor(created), cancellationToken);
        }
    }

    private static ConfigImport Refused(ConfigProblem problem) => new([problem], [], [], []);

    private static async Task<ConfigFile?> ReadAsync(Stream upload, CancellationToken cancellationToken)
    {
        // Read up to one byte past the limit, so a larger file is told apart from one that fits exactly.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await upload.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxFileBytes)
            {
                return null;
            }
        }

        try
        {
            var file = JsonSerializer.Deserialize<ConfigFile>(buffer.ToArray(), Json);
            return file is { Format: FormatName, Version: Version } ? file : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // What the editors cannot see one item at a time: limits, repeated names, and references between sections and sources.
    private static ConfigProblem? Check(ConfigFile file)
    {
        var sources = file.Sources ?? [];
        var profiles = file.Editions ?? [];
        if (sources.Count > MaxSources || profiles.Count is 0 or > MaxEditions || profiles.Any(e => (e.Sections?.Count ?? 0) > MaxSections))
        {
            return new ConfigProblem(FileItem, "TooLarge");
        }

        if (sources.Any(s => s is null || string.IsNullOrWhiteSpace(s.Name) || !Enum.IsDefined(s.Kind))
            || profiles.Any(e => e is null || e.Title is null || (e.Sections ?? []).Any(s => s is null || s.Name is null || !Enum.IsDefined(s.Kind))))
        {
            return new ConfigProblem(FileItem, "Format");
        }

        if (sources.GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } source)
        {
            return new ConfigProblem(source.Key, "Duplicate");
        }

        if (profiles.GroupBy(e => e.Title.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } edition)
        {
            return new ConfigProblem(edition.Key, "Duplicate");
        }

        var names = sources.Select(s => s.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = profiles.SelectMany(e => e.Sections ?? []).SelectMany(s => s.Sources ?? []).FirstOrDefault(n => n is null || !names.Contains(n.Trim()));
        return unknown is null && profiles.SelectMany(e => e.Sections ?? []).All(s => s.Sources is null || s.Sources.All(n => n is not null))
            ? null
            : new ConfigProblem(unknown ?? FileItem, "UnknownSource");
    }

    // Returns the ids by name for the sections; sources not in the file are deleted at the end.
    private async Task<Dictionary<string, Guid>> ImportSourcesAsync(Guid userId, IReadOnlyList<SourceConfig> wanted, List<ConfigProblem> problems, List<string> needLogin, CancellationToken cancellationToken)
    {
        var existing = (await catalog.ListAsync(userId, cancellationToken)).ToList();
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in wanted)
        {
            var name = config.Name.Trim();
            var match = existing.Find(s => s.Kind == config.Kind && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            Guid? id = match?.Id;
            if (match is null)
            {
                switch (await AddAsync(userId, name, config, cancellationToken))
                {
                    case SourceSaveResult.Saved saved:
                        id = saved.Source.Id;
                        break;
                    case SourceSaveResult.Invalid invalid:
                        problems.Add(new ConfigProblem(name, invalid.Reason));
                        continue;
                    default:
                        needLogin.Add(name); // FreshRSS, Karakeep, newsletters: added with their login on the sources page
                        continue;
                }
            }
            else if (config.Kind == SourceKind.Rss && !await MoveFeedAsync(match.Id, config.Url, cancellationToken))
            {
                problems.Add(new ConfigProblem(name, "InvalidUrl"));
                continue;
            }

            if (await catalog.SaveSettingsAsync(userId, id!.Value, Settings(name, config), cancellationToken) is SourceSaveResult.Invalid refused) // ! is safe: set by the match or the add above
            {
                problems.Add(new ConfigProblem(name, refused.Reason));
                continue;
            }

            ids[name] = id.Value;
        }

        if (problems.Count == 0)
        {
            foreach (var source in existing.Where(s => !ids.ContainsValue(s.Id)))
            {
                await catalog.DeleteAsync(userId, source.Id, cancellationToken);
            }
        }

        return ids;
    }

    // Null for the kinds that need a login: they are never created from a file.
    private Task<SourceSaveResult?> AddAsync(Guid userId, string name, SourceConfig config, CancellationToken cancellationToken) => config.Kind switch
    {
        SourceKind.Rss => Nullable(catalog.AddFeedAsync(userId, name, config.Url ?? string.Empty, config.FetchFullText, cancellationToken)),
        SourceKind.Local => Nullable(catalog.AddLocalAsync(userId, name, config.Place ?? string.Empty, config.Country, cancellationToken)),
        SourceKind.HackerNews => Nullable(catalog.AddHackerNewsAsync(userId, config.MinScore ?? 0, cancellationToken)),
        SourceKind.Reddit => Nullable(catalog.AddRedditAsync(userId, config.Url ?? name, config.MinScore ?? 0, cancellationToken)),
        _ => Task.FromResult<SourceSaveResult?>(null),
    };

    private static async Task<SourceSaveResult?> Nullable(Task<SourceSaveResult> save) => await save;

    // A feed kept by its name may have moved; the address is checked like a new one.
    private async Task<bool> MoveFeedAsync(Guid sourceId, string? url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var feed) || UrlPolicy.Check(feed) is not null)
        {
            return false;
        }

        var source = await db.Sources.SingleAsync(s => s.Id == sourceId, cancellationToken);
        source.Url = feed.AbsoluteUri;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static SourceSettings Settings(string name, SourceConfig config) => new(
        name,
        config.FetchFullText,
        config.MaxArticles,
        [.. (config.IncludeKeywords ?? []).Where(k => k is not null)],
        [.. (config.ExcludeKeywords ?? []).Where(k => k is not null)],
        config.MinWords,
        config.SkipLiveTickers,
        config.LocalAiOnly,
        config.SkipAds,
        config.MarkReadAfterDelivery,
        config.LoadImages,
        config.Sender,
        config.SkipMedia,
        config.MinScore,
        config.MaxWords,
        config.ExcludeTitlePattern);

    private async Task ImportEditionsAsync(Guid userId, IReadOnlyList<EditionConfig> wanted, Dictionary<string, Guid> sourceIds, List<ConfigProblem> problems, List<string> needToken, CancellationToken cancellationToken)
    {
        var existing = (await editions.ListAsync(userId, cancellationToken)).ToList();
        var kept = new HashSet<Guid>();
        foreach (var config in wanted)
        {
            var title = config.Title.Trim();
            var profile = existing.Find(p => string.Equals(p.Title, title, StringComparison.OrdinalIgnoreCase) && !kept.Contains(p.Id));
            if (profile is null)
            {
                profile = await editions.CreateAsync(userId, title, cancellationToken);
                _created.Add(profile.Id);
            }

            kept.Add(profile.Id);

            var days = config.Days is { Count: > 0 } chosen ? chosen : EveryDay;
            // A new edition has no recipient yet while sending by mail, which the form never saves but the app starts
            // with; saved as fetch-only, then set as it was, so an exported fresh edition comes back the same.
            var recipient = config.Recipient?.Trim() ?? string.Empty;
            var waitsForRecipient = config.SendByEmail && recipient.Length == 0;
            var settings = new ProfileSettings(title, recipient, config.DeliveryTime ?? string.Empty, config.TimeZone ?? string.Empty, config.Region, config.Country, days, TimeSpan.FromMinutes(config.LeadMinutes), config.SendByEmail && !waitsForRecipient, config.MinArticles);
            problems.AddRange((await editions.SaveSettingsAsync(userId, profile.Id, settings, cancellationToken)).Select(field => new ConfigProblem(title, field)));
            if (waitsForRecipient)
            {
                profile.SendByEmail = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            var interests = (config.Interests ?? []).Select(i => new Interest { Topic = i?.Topic ?? string.Empty, Keywords = [.. (i?.Keywords ?? []).Where(k => k is not null)], Weight = i?.Weight ?? InterestWeight.Prefer }).ToList();
            if (!await editions.SaveInterestsAsync(userId, profile.Id, interests, cancellationToken))
            {
                problems.Add(new ConfigProblem(title, "Interests"));
            }

            await ImportNotificationAsync(userId, profile.Id, title, config, problems, needToken, cancellationToken);
            if (!await ImportSectionsAsync(userId, profile.Id, config.Sections ?? [], sourceIds, cancellationToken))
            {
                problems.Add(new ConfigProblem(title, "Sections"));
            }
        }

        if (problems.Count > 0)
        {
            return;
        }

        // Last, once all else is in: deleting an edition also deletes its issue files, which no rollback brings back.
        foreach (var profile in existing.Where(p => !kept.Contains(p.Id)))
        {
            await editions.DeleteAsync(userId, profile.Id, cancellationToken);
        }
    }

    // A token is never in the file: where the channel needs one the edition does not have, it stays silent until entered.
    private async Task ImportNotificationAsync(Guid userId, Guid profileId, string title, EditionConfig config, List<ConfigProblem> problems, List<string> needToken, CancellationToken cancellationToken)
    {
        var errors = await editions.SaveNotificationAsync(userId, profileId, new NotificationSettings(config.Notify, config.NotifyTarget, null), cancellationToken);
        if (errors.SequenceEqual([NotifySecretField]))
        {
            await editions.SaveNotificationAsync(userId, profileId, new NotificationSettings(NotificationChannel.None, null, null), cancellationToken);
            needToken.Add(title);
            return;
        }

        problems.AddRange(errors.Select(field => new ConfigProblem(title, field)));
    }

    // The sections are replaced as a whole, in the file's order; the same checks as the section editor.
    private async Task<bool> ImportSectionsAsync(Guid userId, Guid profileId, IReadOnlyList<SectionConfig> wanted, Dictionary<string, Guid> sourceIds, CancellationToken cancellationToken)
    {
        var valid = wanted.All(s => s.Name.Trim().Length is > 0 and <= ProfileEditor.MaxNameLength && s.MaxArticles is null or (>= 1 and <= MaxSectionArticles))
            && wanted.Where(s => s.Kind != SectionKind.Articles).GroupBy(s => s.Kind).All(g => g.Count() == 1);
        if (!valid || await editions.FindAsync(userId, profileId, cancellationToken) is not { } profile)
        {
            return false;
        }

        var ids = sourceIds.Values.ToList();
        var sources = await db.Sources.Where(s => s.UserId == userId && ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        profile.Sections.Clear();
        for (var i = 0; i < wanted.Count; i++)
        {
            var config = wanted[i];
            var section = new Section
            {
                Name = config.Name.Trim(),
                Kind = config.Kind,
                Enabled = config.Enabled,
                Position = i + 1,
                MaxArticles = config.Kind == SectionKind.Articles ? config.MaxArticles : null,
                ShowAnniversaries = config.Anniversaries,
                ShowFeaturedArticle = config.FeaturedArticle,
            };
            if (config.Kind == SectionKind.Articles)
            {
                foreach (var name in (config.Sources ?? []).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    section.Sources.Add(sources[sourceIds[name]]);
                }
            }

            profile.Sections.Add(section);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Each store checks its own part, as on the settings page; what one refuses does not hold up the others.
    private async Task<List<ConfigProblem>> ImportInstanceAsync(Guid adminId, InstanceConfig instance, CancellationToken cancellationToken)
    {
        var problems = new List<ConfigProblem>();
        if (instance.Mail is { } m)
        {
            var input = new MailSettingsInput(m.Host, m.Port.ToString(CultureInfo.InvariantCulture), Enum.IsDefined(m.Security) ? m.Security : MailSecurity.StartTls, m.Username, null, m.FromAddress, m.FromName);
            problems.AddRange((await mail.SaveAsync(input, cancellationToken)).Select(field => new ConfigProblem("Mail", field)));
        }

        if (instance.Ai is { } a)
        {
            var input = new AiSettingsInput(a.Endpoint, a.Model, null, a.IsLocal, a.Summaries, a.Enabled, a.DailyLimit, a.SummaryPrompt, a.OverviewPrompt, a.NeutralHeadlines, a.HeadlinesPrompt, a.InterestsPrompt, a.RecognizeInterests);
            problems.AddRange((await ai.SaveAsync(input, adminId, cancellationToken)).Select(field => new ConfigProblem("AI", field)));
        }

        if (!newsSearch.IsLocked && !await newsSearch.SaveAsync(instance.SearxngUrl, adminId, cancellationToken))
        {
            problems.Add(new ConfigProblem("SearXNG", "InvalidUrl"));
        }

        var hosts = (instance.AllowedHosts ?? []).Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().ToLowerInvariant()).Distinct().ToList();
        if (hosts.Count > MaxAllowedHosts || hosts.Any(h => h.Length > MaxHostLength))
        {
            problems.Add(new ConfigProblem("AllowedHosts", "TooLarge"));
            return problems;
        }

        // Replaced like the rest, but after the AI and SearXNG, whose hosts they add themselves.
        foreach (var host in hosts)
        {
            await allowlist.AddAsync(host, adminId, cancellationToken);
        }

        var keep = hosts.Concat(ServiceHosts(instance)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in allowlist.Stored.Where(h => !keep.Contains(h)).ToList())
        {
            await allowlist.RemoveAsync(stale, cancellationToken);
        }

        return problems;
    }

    // The hosts the AI and SearXNG settings allow on their own, as entered on the settings page.
    private static IEnumerable<string> ServiceHosts(InstanceConfig instance) =>
        new[] { instance.Ai is { IsLocal: true } local ? local.Endpoint : null, instance.SearxngUrl }
            .Select(u => Uri.TryCreate(u?.Trim(), UriKind.Absolute, out var url) ? $"{url.Host}:{url.Port}".ToLowerInvariant() : null)
            .OfType<string>();

    private static SourceConfig Export(Source s) => new(
        s.Name,
        s.Kind,
        s.Url,
        s.Place,
        s.Country,
        s.Stream,
        s.FetchFullText,
        s.MaxArticles,
        s.IncludeKeywords,
        s.ExcludeKeywords,
        s.ExcludeTitlePattern,
        s.MinWords,
        s.MaxWords,
        s.SkipLiveTickers,
        s.SkipAds,
        s.SkipMedia,
        s.LoadImages,
        s.MarkReadAfterDelivery,
        s.LocalAiOnly,
        s.MinScore,
        s.Sender);

    private static EditionConfig Export(Profile p) => new(
        p.Title,
        p.Recipient,
        p.DeliveryTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
        p.TimeZoneId,
        p.SendByEmail,
        (int)p.LeadTime.TotalMinutes,
        p.Days,
        p.MinArticles,
        p.Region,
        p.Country,
        p.NotifyChannel,
        p.NotifyTarget,
        [.. p.Interests.Select(i => new InterestConfig(i.Topic, i.Keywords, i.Weight))],
        [.. p.Sections.OrderBy(s => s.Position).Select(s => new SectionConfig(
            s.Name,
            s.Kind,
            s.Enabled,
            s.MaxArticles,
            s.Kind == SectionKind.Articles ? [.. s.Sources.Select(source => source.Name).Order(StringComparer.Ordinal)] : null,
            s.ShowAnniversaries,
            s.ShowFeaturedArticle))]);

    private async Task<InstanceConfig> ExportInstanceAsync(CancellationToken cancellationToken)
    {
        var m = await mail.LoadAsync(cancellationToken);
        var a = await ai.LoadAsync(cancellationToken);
        var searxng = newsSearch.IsLocked ? null : await newsSearch.SearxngUrlAsync(cancellationToken);
        return new InstanceConfig(
            new MailConfig(m.Host, m.Port, m.Security, m.Username, m.FromAddress, m.FromName),
            new AiConfig(a.Enabled, a.Endpoint, a.Model, a.IsLocal, a.Summaries, a.DailyLimit, a.RecognizeInterests, a.NeutralHeadlines, a.SummaryPrompt, a.OverviewPrompt, a.HeadlinesPrompt, a.InterestsPrompt),
            searxng,
            [.. allowlist.Stored.Order(StringComparer.Ordinal)]);
    }
}
