using MorningInk.Domain;
using MorningInk.Infrastructure.Sources;

namespace MorningInk.Infrastructure.Profiles;

/// <summary>What the setup wizard should add.</summary>
/// <param name="SectionNames">Section name per topic in the user's language, e.g. News: "Nachrichten".</param>
/// <param name="Local">Local news for the user's region; null when they did not want them.</param>
public sealed record StarterChoice(IReadOnlyCollection<SampleSource> Feeds, IReadOnlyDictionary<SampleTopic, string> SectionNames, LocalStarter? Local);

public sealed record LocalStarter(string Name, string Place, string? Country, string SectionName);

/// <summary>Adds the sources picked in the setup wizard and puts them into sections, one per topic.</summary>
/// <remarks>Safe to run again: sources the user already has and sections that already exist are reused.</remarks>
public sealed class Onboarding(SourceCatalog catalog, ProfileEditor profiles)
{
    public async Task AddStartersAsync(Guid userId, StarterChoice choice, CancellationToken cancellationToken)
    {
        var existing = await catalog.ListAsync(userId, cancellationToken);
        var bySection = new Dictionary<string, List<Guid>>();

        foreach (var feed in choice.Feeds)
        {
            var url = new Uri(feed.Url).AbsoluteUri;
            var source = existing.FirstOrDefault(s => s.Url == url) ?? Saved(await catalog.AddFeedAsync(userId, feed.Name, url, fetchFullText: true, cancellationToken));
            Add(bySection, choice.SectionNames[feed.Topic], source);
        }

        if (choice.Local is { } local)
        {
            var source = existing.FirstOrDefault(s => s.Kind == SourceKind.Local && s.Place == local.Place.Trim() && s.Country == local.Country)
                ?? Saved(await catalog.AddLocalAsync(userId, local.Name, local.Place, local.Country, cancellationToken));
            Add(bySection, local.SectionName, source);
        }

        foreach (var (name, sourceIds) in bySection)
        {
            var section = await FindOrAddSectionAsync(userId, name, cancellationToken);
            var all = section.Sources.Select(s => s.Id).Union(sourceIds).ToList();
            await profiles.SaveSectionAsync(userId, section.Id, section.Name, section.MaxArticles, all, cancellationToken);
        }
    }

    private static void Add(Dictionary<string, List<Guid>> bySection, string section, Source? source)
    {
        if (source is null)
        {
            return;
        }

        if (!bySection.TryGetValue(section, out var ids))
        {
            bySection[section] = ids = [];
        }

        ids.Add(source.Id);
    }

    // The samples are fixed and valid; a place the user typed may not be, and then local news are left out.
    private static Source? Saved(SourceSaveResult result) => (result as SourceSaveResult.Saved)?.Source;

    private async Task<Section> FindOrAddSectionAsync(Guid userId, string name, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetOrCreateAsync(userId, cancellationToken);
        if (profile.Sections.FirstOrDefault(s => s.Name == name) is { } section)
        {
            return section;
        }

        await profiles.AddSectionAsync(userId, profile.Id, name, cancellationToken);
        return (await profiles.GetOrCreateAsync(userId, cancellationToken)).Sections.Single(s => s.Name == name);
    }
}
