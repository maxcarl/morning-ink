using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Publishing;
using MorningInk.Domain;

namespace MorningInk.Infrastructure.Tests.Persistence;

public sealed class PublishingStoreTests : IDisposable
{
    private readonly TestHost _host = new();

    [Theory]
    [InlineData("DE", "en", "de")] // the edition's country wins: German weather for Munich
    [InlineData("AT", "en", "de")]
    [InlineData("US", "de", "en")]
    [InlineData("FR", "de", "de")] // no French issue texts yet: the user's language, not a mix
    [InlineData(null, "de", "de")] // worldwide: the user's language
    public async Task Issue_language_follows_the_editions_country_when_issues_exist_in_it(string? country, string userLanguage, string expected)
    {
        await using var services = await _host.StartAsync();
        Guid profileId;
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInk.Infrastructure.Persistence.MorningInkDbContext>();
            var user = new User { Name = "alex", Language = userLanguage };
            var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = "alex@kindle.com", Country = country };
            db.AddRange(user, profile);
            await db.SaveChangesAsync();
            profileId = profile.Id;
        }

        await using var read = services.CreateAsyncScope();
        var loaded = await read.ServiceProvider.GetRequiredService<IPublishingStore>().LoadProfileAsync(profileId, CancellationToken.None);

        Assert.Equal(expected, loaded?.Language);
    }

    public void Dispose() => _host.Dispose();
}
