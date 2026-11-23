using MorningInk.Domain;

namespace MorningInk.Application.Publishing;

/// <summary>Data access for publishing. Changes to loaded objects are saved by <see cref="SaveChangesAsync"/>.</summary>
public interface IPublishingStore
{
    /// <summary>Loads the profile with its sections and their sources, plus the owner's language; null if missing.</summary>
    Task<PublishingProfile?> LoadProfileAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>When every profile wants its issue, for the scheduler.</summary>
    Task<IReadOnlyList<ProfileSchedule>> ListSchedulesAsync(CancellationToken cancellationToken);

    /// <summary>Finds the issue of a profile for a slot, with its entries.</summary>
    Task<Issue?> FindIssueAsync(Guid profileId, DateTimeOffset scheduledFor, CancellationToken cancellationToken);

    /// <summary>The number the next issue of the profile gets.</summary>
    Task<int> NextIssueNumberAsync(Guid profileId, CancellationToken cancellationToken);

    void AddIssue(Issue issue);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>A profile's delivery time in its time zone.</summary>
/// <param name="Days">Weekdays with an issue; null or empty means every day.</param>
/// <param name="LeadTime">How long before delivery the issue is built; zero builds at delivery.</param>
public sealed record ProfileSchedule(Guid ProfileId, TimeOnly DeliveryTime, string TimeZoneId, IReadOnlyCollection<DayOfWeek>? Days = null, TimeSpan LeadTime = default);

/// <param name="Profile">The profile, with sections and sources loaded.</param>
/// <param name="Language">Culture name of the owner, e.g. "de", for texts in the issue.</param>
public sealed record PublishingProfile(Profile Profile, string Language)
{
    /// <summary>
    /// What the reader said about each source lately, "more" counting up and "less" down; sources with more come
    /// first within a section, after the edition's interests.
    /// </summary>
    public IReadOnlyDictionary<Guid, int> SourceLeanings { get; init; } = new Dictionary<Guid, int>();
}
