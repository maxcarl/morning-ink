using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>What the "on this day" section brings: the day's anniversaries, Wikipedia's article of the day, or both.</summary>
public sealed record OnThisDayParts(bool Anniversaries, bool FeaturedArticle);

/// <summary>
/// Edits the sections of a user's editions: add, fill with sources, order, delete. Every method only reaches the
/// given user's editions, and sections only ever get that user's sources.
/// </summary>
public sealed class SectionEditor(MorningInkDbContext db, ProfileEditor profiles)
{
    private const int MaxSectionArticles = 100;

    public async Task<bool> AddSectionAsync(Guid userId, Guid profileId, string name, CancellationToken cancellationToken)
    {
        name = name.Trim();
        if (name.Length is 0 or > ProfileEditor.MaxNameLength || await profiles.FindAsync(userId, profileId, cancellationToken) is not { } profile)
        {
            return false;
        }

        AddAtEnd(profile, new Section { Name = name });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Adds the overview, the weather or "on this day" at the end of the order (before a closing "on this day"); each kind once per edition.</summary>
    /// <returns>False for an articles section, a kind the edition has already, or an edition not the user's.</returns>
    public async Task<bool> AddSpecialAsync(Guid userId, Guid profileId, SectionKind kind, string name, CancellationToken cancellationToken)
    {
        name = name.Trim();
        if (kind == SectionKind.Articles || name.Length is 0 or > ProfileEditor.MaxNameLength
            || await profiles.FindAsync(userId, profileId, cancellationToken) is not { } profile
            || profile.Sections.Any(s => s.Kind == kind))
        {
            return false;
        }

        AddAtEnd(profile, new Section { Name = name, Kind = kind });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Switches a section off and on; off, it keeps its place and settings but is left out of the issue.</summary>
    public async Task<bool> SetEnabledAsync(Guid userId, Guid sectionId, bool enabled, CancellationToken cancellationToken)
    {
        if (await OwnerOfSectionAsync(userId, sectionId, cancellationToken) is not { } profile)
        {
            return false;
        }

        profile.Sections.Single(s => s.Id == sectionId).Enabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <param name="sourceIds">Sources to put into the section; ids that are not the user's own are ignored.</param>
    /// <param name="parts">What "on this day" brings; with both off it is left out of the issue. Null keeps it, other kinds ignore it.</param>
    public async Task<bool> SaveSectionAsync(Guid userId, Guid sectionId, string name, int? maxArticles, IReadOnlyCollection<Guid> sourceIds, OnThisDayParts? parts, CancellationToken cancellationToken)
    {
        name = name.Trim();
        var section = (await OwnerOfSectionAsync(userId, sectionId, cancellationToken))?.Sections.Single(s => s.Id == sectionId);
        if (section is null || name.Length is 0 or > ProfileEditor.MaxNameLength || maxArticles is < 1 or > MaxSectionArticles)
        {
            return false;
        }

        // The overview, the weather and "on this day" have no sources and no limit; "on this day" has its parts.
        if (section.Kind != SectionKind.Articles)
        {
            section.Name = name;
            if (section.Kind == SectionKind.OnThisDay && parts is not null)
            {
                section.ShowAnniversaries = parts.Anniversaries;
                section.ShowFeaturedArticle = parts.FeaturedArticle;
            }

            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        // Only the user's own sources, whatever the form sent.
        var ids = sourceIds.ToList();
        var sources = await db.Sources.Where(s => s.UserId == userId && ids.Contains(s.Id)).ToListAsync(cancellationToken);
        section.Name = name;
        section.MaxArticles = maxArticles;
        section.Sources.Clear();
        sources.ForEach(section.Sources.Add);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteSectionAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken)
    {
        if (await OwnerOfSectionAsync(userId, sectionId, cancellationToken) is not { } profile)
        {
            return false;
        }

        profile.Sections.Remove(profile.Sections.Single(s => s.Id == sectionId));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Moves the section to the given place in the order (0 is the top), e.g. after drag and drop.</summary>
    /// <remarks>An index outside the list puts it first or last, so a stale page can never lose a section.</remarks>
    public async Task<bool> MoveSectionAsync(Guid userId, Guid sectionId, int index, CancellationToken cancellationToken)
    {
        if (await OwnerOfSectionAsync(userId, sectionId, cancellationToken) is not { } profile)
        {
            return false;
        }

        var ordered = profile.Sections.OrderBy(s => s.Position).ToList();
        var section = ordered.Single(s => s.Id == sectionId);

        ordered.Remove(section);
        ordered.Insert(Math.Clamp(index, 0, ordered.Count), section);

        // Renumbered from 1, so positions stay dense whatever happened before.
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Position = i + 1;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // "On this day" closes the issue: while it is last, a new section goes in before it.
    private static void AddAtEnd(Profile profile, Section section)
    {
        var last = profile.Sections.MaxBy(s => s.Position);
        section.Position = (last?.Position ?? 0) + 1;
        if (last?.Kind == SectionKind.OnThisDay)
        {
            (section.Position, last.Position) = (last.Position, section.Position);
        }

        profile.Sections.Add(section);
    }

    // The user's profile that holds the section; null when no profile of theirs does.
    private Task<Profile?> OwnerOfSectionAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken) =>
        profiles.Query(userId).SingleOrDefaultAsync(p => p.Sections.Any(s => s.Id == sectionId), cancellationToken);
}
