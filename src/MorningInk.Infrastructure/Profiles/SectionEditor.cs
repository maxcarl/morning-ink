using Microsoft.EntityFrameworkCore;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;

namespace MorningInk.Infrastructure.Profiles;

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

        var position = profile.Sections.Select(s => s.Position).DefaultIfEmpty(0).Max() + 1;
        profile.Sections.Add(new Section { Name = name, Position = position });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <param name="sourceIds">Sources to put into the section; ids that are not the user's own are ignored.</param>
    public async Task<bool> SaveSectionAsync(Guid userId, Guid sectionId, string name, int? maxArticles, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken)
    {
        name = name.Trim();
        var section = (await OwnerOfSectionAsync(userId, sectionId, cancellationToken))?.Sections.Single(s => s.Id == sectionId);
        if (section is null || name.Length is 0 or > ProfileEditor.MaxNameLength || maxArticles is < 1 or > MaxSectionArticles)
        {
            return false;
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

    // The user's profile that holds the section; null when no profile of theirs does.
    private Task<Profile?> OwnerOfSectionAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken) =>
        profiles.Query(userId).SingleOrDefaultAsync(p => p.Sections.Any(s => s.Id == sectionId), cancellationToken);
}
