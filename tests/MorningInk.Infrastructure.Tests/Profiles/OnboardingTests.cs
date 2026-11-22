using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Profiles;

public sealed class OnboardingTests : IDisposable
{
    private static readonly Dictionary<SampleTopic, string> Names = new() { [SampleTopic.News] = "Nachrichten", [SampleTopic.Tech] = "Technik" };

    private readonly TestHost _host = new();

    [Fact]
    public async Task Starters_become_sources_in_one_section_per_topic()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services);
        var picked = SampleSources.All.Where(s => s.Name is "tagesschau" or "heise online" or "Golem.de").ToList();

        await AddAsync(services, user, new StarterChoice(picked, Names, new LocalStarter("Lokales 52062", "52062", "DE", "Region")));

        var sections = await SectionsAsync(services, user);
        Assert.Equal(["Nachrichten", "Technik", "Region"], sections.Select(s => s.Name));
        Assert.Equal(["tagesschau"], sections[0].Sources.Select(s => s.Name));
        Assert.Equal(["Golem.de", "heise online"], sections[1].Sources.Select(s => s.Name).Order());
        Assert.Equal(SourceKind.Local, Assert.Single(sections[2].Sources).Kind);
    }

    [Fact]
    public async Task Running_the_wizard_again_adds_nothing_twice()
    {
        await using var services = await _host.StartAsync();
        var user = await CreateUserAsync(services);
        var choice = new StarterChoice(SampleSources.All.Take(2).ToList(), Names, new LocalStarter("Lokales 52062", "52062", "DE", "Region"));

        await AddAsync(services, user, choice);
        await AddAsync(services, user, choice);

        await using var scope = services.CreateAsyncScope();
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Sources.CountAsync(s => s.UserId == user));
        Assert.All(await SectionsAsync(services, user), section => Assert.Equal(section.Sources.Count, section.Sources.DistinctBy(s => s.Id).Count()));
    }

    [Fact]
    public void Every_sample_is_a_plain_https_feed()
    {
        Assert.NotEmpty(SampleSources.All);
        Assert.All(SampleSources.All, sample => Assert.StartsWith("https://", sample.Url, StringComparison.Ordinal));
        Assert.Equal(SampleSources.All.Count, SampleSources.All.DistinctBy(s => s.Url).Count());
    }

    [Theory]
    [InlineData("de", "DE", "tagesschau", "heise online")]
    [InlineData("de", "AT", "ORF", "heise online")] // the country's own news, tech in the language
    [InlineData("fr", "FR", "Le Monde", "Numerama")]
    [InlineData("en", "US", "NPR", "Ars Technica")]
    [InlineData("ja", "JP", "NHK", "Ars Technica")] // no tech feed in Japanese: English
    [InlineData("sv", null, "Deutsche Welle", "Ars Technica")] // neither country nor language known: international English
    public void One_suggestion_per_topic_fits_country_and_language(string language, string? country, string news, string tech) =>
        Assert.Equal([news, tech], SampleSources.Suggest(language, country).Select(s => s.Name));

    public void Dispose() => _host.Dispose();

    private static async Task AddAsync(IServiceProvider services, Guid user, StarterChoice choice)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<Onboarding>().AddStartersAsync(user, choice, CancellationToken.None);
    }

    private static async Task<List<Section>> SectionsAsync(IServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles
            .Where(p => p.UserId == user).SelectMany(p => p.Sections).Include(s => s.Sources).OrderBy(s => s.Position).ToListAsync();
    }

    private static async Task<Guid> CreateUserAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "max" };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
