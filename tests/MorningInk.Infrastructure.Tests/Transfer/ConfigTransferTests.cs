using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Delivery;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Profiles;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Persistence;
using MorningInk.Infrastructure.Transfer;

namespace MorningInk.Infrastructure.Tests.Transfer;

public sealed class ConfigTransferTests : IDisposable
{
    private readonly TestHost _host = new();

    [Fact]
    public async Task A_setup_moves_to_another_user_as_it_was_without_any_secret()
    {
        await using var services = await _host.StartAsync();
        var alex = await CreateUserAsync(services, "alex");
        await SetUpAsync(services, alex);

        var json = await UseAsync(services, t => t.ExportAsync(alex, isAdmin: false, CancellationToken.None));
        var erika = await CreateUserAsync(services, "erika");
        var import = await ImportAsync(services, erika, json);

        Assert.True(import.Imported, string.Join(", ", import.Problems));
        Assert.Contains("\"cron\": \"30 6 * * 1-5\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("geheim", json, StringComparison.Ordinal); // the notification token stays here
        Assert.DoesNotContain("instance", json, StringComparison.Ordinal); // only for admins
        Assert.Equal([], import.NeedLogin);
        Assert.Equal(["Morgen"], import.NeedToken); // Gotify without its token is off until entered
        var expected = json
            .Replace("\"notify\": \"Gotify\"", "\"notify\": \"None\"", StringComparison.Ordinal)
            .Replace("      \"notifyTarget\": \"https://gotify.example.com\",\n", string.Empty, StringComparison.Ordinal);
        Assert.Equal(expected, await UseAsync(services, t => t.ExportAsync(erika, isAdmin: false, CancellationToken.None)));
    }

    [Fact]
    public async Task An_import_replaces_the_setup_and_keeps_what_it_matches()
    {
        await using var services = await _host.StartAsync();
        var alex = await CreateUserAsync(services, "alex");
        await SetUpAsync(services, alex);
        var heise = (await SourcesAsync(services, alex)).Single(s => s.Name == "Heise").Id;
        var morning = (await EditionsAsync(services, alex)).Single().Id;

        var import = await ImportAsync(services, alex, """
            {
              "format": "morning-ink-config", "version": 1,
              "sources": [ { "name": "Heise", "kind": "Rss", "url": "https://www.heise.de/rss/heise.rdf", "maxArticles": 5, "excludeKeywords": ["Anzeige"] } ],
              "editions": [
                { "title": "Morgen", "recipient": "name_abc@kindle.com", "deliveryTime": "07:00", "timeZone": "Europe/Berlin",
                  "sections": [ { "name": "Tech", "sources": ["Heise"] } ] },
                { "title": "Abend", "recipient": "", "sendByEmail": false, "deliveryTime": "19:00", "timeZone": "Europe/Berlin" }
              ]
            }
            """);

        Assert.True(import.Imported, string.Join(", ", import.Problems));
        var source = Assert.Single(await SourcesAsync(services, alex)); // the local news went
        Assert.Equal((heise, 5, "Anzeige"), (source.Id, source.MaxArticles, source.ExcludeKeywords.Single())); // same source, so its archive and health stay
        var editions = await EditionsAsync(services, alex);
        Assert.Equal(["Morgen", "Abend"], editions.Select(e => e.Title));
        Assert.Equal(morning, editions[0].Id);
        var tech = Assert.Single(editions[0].Sections);
        Assert.Equal(("Tech", heise), (tech.Name, tech.Sources.Single().Id));
    }

    [Theory]
    [InlineData("""{ "format": "morning-ink-config", "version": 1, "editions": [ { "title": "Morgen", "recipient": "", "sendByEmail": false, "deliveryTime": "07:00", "timeZone": "Nowhere/Town" } ] }""", "Morgen", "TimeZone")]
    [InlineData("""{ "format": "morning-ink-config", "version": 1, "sources": [ { "name": "Intern", "kind": "Rss", "url": "file:///etc/passwd" } ], "editions": [ { "title": "Morgen", "recipient": "", "sendByEmail": false, "deliveryTime": "07:00", "timeZone": "Europe/Berlin" } ] }""", "Intern", "InvalidUrl")]
    [InlineData("""{ "format": "morning-ink-config", "version": 1, "editions": [ { "title": "Morgen", "recipient": "", "sendByEmail": false, "deliveryTime": "07:00", "timeZone": "Europe/Berlin", "sections": [ { "name": "Tech", "sources": ["Fehlt"] } ] } ] }""", "Fehlt", "UnknownSource")]
    [InlineData("""{ "format": "morning-ink-config", "version": 1, "editions": [ { "title": "Morgen", "recipient": "", "sendByEmail": false, "deliveryTime": "07:00", "timeZone": "Europe/Berlin", "cron": "* * * * *" } ] }""", "Morgen", "Cron")]
    [InlineData("""{ "format": "morning-ink-config", "version": 1, "editions": [] }""", "file", "TooLarge")]
    [InlineData("""{ "format": "something-else", "version": 1 }""", "file", "Format")]
    [InlineData("not json", "file", "Format")]
    public async Task A_file_with_one_problem_changes_nothing(string json, string item, string code)
    {
        await using var services = await _host.StartAsync();
        var alex = await CreateUserAsync(services, "alex");
        await SetUpAsync(services, alex);
        var before = await UseAsync(services, t => t.ExportAsync(alex, isAdmin: false, CancellationToken.None));

        var import = await ImportAsync(services, alex, json);

        Assert.Contains(new ConfigProblem(item, code), import.Problems);
        Assert.Equal(before, await UseAsync(services, t => t.ExportAsync(alex, isAdmin: false, CancellationToken.None)));
    }

    [Fact]
    public async Task Sources_that_need_a_login_are_kept_when_matched_and_named_when_not()
    {
        await using var services = await _host.StartAsync();
        var alex = await CreateUserAsync(services, "alex");

        var import = await ImportAsync(services, alex, """
            { "format": "morning-ink-config", "version": 1,
              "sources": [ { "name": "Mein FreshRSS", "kind": "FreshRss", "url": "https://rss.example.com", "stream": "user/-/label/Tech" } ],
              "editions": [ { "title": "Morgen", "recipient": "", "sendByEmail": false, "deliveryTime": "07:00", "timeZone": "Europe/Berlin", "sections": [ { "name": "Tech" } ] } ] }
            """);

        Assert.True(import.Imported, string.Join(", ", import.Problems));
        Assert.Equal(["Mein FreshRSS"], import.NeedLogin);
        Assert.Empty(await SourcesAsync(services, alex));
    }

    [Fact]
    public async Task An_admins_file_carries_the_instance_without_the_mail_password()
    {
        await using var services = await _host.StartAsync();
        var alex = await CreateUserAsync(services, "alex");
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<MailSettingsStore>().SaveAsync(new MailSettingsInput("smtp.example.com", "587", MailSecurity.StartTls, "news", "streng-geheim", "news@example.com", null), CancellationToken.None);
        }

        var json = await UseAsync(services, t => t.ExportAsync(alex, isAdmin: true, CancellationToken.None));
        Assert.Contains("smtp.example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("streng-geheim", json, StringComparison.Ordinal);

        var import = await ImportAsync(services, alex, json.Replace("smtp.example.com", "mail.example.org", StringComparison.Ordinal), isAdmin: true);

        Assert.True(import.Imported, string.Join(", ", import.Problems)); // a fresh edition without recipient comes back too
        Assert.Empty(import.InstanceProblems);
        await using var check = services.CreateAsyncScope();
        var mail = await check.ServiceProvider.GetRequiredService<MailSettingsStore>().LoadAsync(CancellationToken.None);
        Assert.Equal(("mail.example.org", "streng-geheim"), (mail.Host, mail.Password)); // the password stays as it was
    }

    public void Dispose() => _host.Dispose();

    // A feed, local news and one edition with a section, an interest and a Gotify notification with its token.
    private static async Task SetUpAsync(IServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<SourceCatalog>();
        var heise = Assert.IsType<SourceSaveResult.Saved>(await catalog.AddFeedAsync(user, "Heise", "https://www.heise.de/rss/heise.rdf", true, CancellationToken.None)).Source;
        await catalog.AddLocalAsync(user, "Lokales", "Musterstadt", "DE", CancellationToken.None);
        await catalog.SaveSettingsAsync(user, heise.Id, new SourceSettings("Heise", true, 8, [], ["Anzeige", "Rabatt"], null, true, ExcludeTitlePattern: "^Live"), CancellationToken.None);

        var editor = scope.ServiceProvider.GetRequiredService<ProfileEditor>();
        var profile = await editor.GetOrCreateAsync(user, CancellationToken.None);
        await editor.SaveSettingsAsync(user, profile.Id, new ProfileSettings("Morgen", "name_abc@kindle.com", "06:30", "Europe/Berlin", "10115", "DE", [DayOfWeek.Monday, DayOfWeek.Friday], TimeSpan.FromMinutes(30), Cron: "30 6 * * 1-5"), CancellationToken.None);
        await editor.SaveInterestsAsync(user, profile.Id, [new Interest { Topic = "Raumfahrt", Keywords = ["Rakete", "ISS"], Weight = InterestWeight.StrongPrefer }], CancellationToken.None);
        await editor.SaveNotificationAsync(user, profile.Id, new NotificationSettings(NotificationChannel.Gotify, "https://gotify.example.com", "geheim"), CancellationToken.None);

        var sections = scope.ServiceProvider.GetRequiredService<SectionEditor>();
        await sections.AddSectionAsync(user, profile.Id, "Tech", CancellationToken.None);
        var tech = (await editor.FindAsync(user, profile.Id, CancellationToken.None))!.Sections.Single(s => s.Name == "Tech"); // ! is safe: just saved
        await sections.SaveSectionAsync(user, tech.Id, "Tech", 5, [heise.Id], null, CancellationToken.None);
    }

    private static async Task<ConfigImport> ImportAsync(IServiceProvider services, Guid user, string json, bool isAdmin = false)
    {
        await using var upload = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await UseAsync(services, t => t.ImportAsync(user, isAdmin, upload, CancellationToken.None));
    }

    private static async Task<T> UseAsync<T>(IServiceProvider services, Func<ConfigTransfer, Task<T>> action)
    {
        await using var scope = services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ConfigTransfer>());
    }

    private static async Task<Guid> CreateUserAsync(IServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = name };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<List<Source>> SourcesAsync(IServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Sources.Where(s => s.UserId == user).AsNoTracking().ToListAsync();
    }

    private static async Task<List<Profile>> EditionsAsync(IServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MorningInkDbContext>().Profiles
            .Where(p => p.UserId == user).OrderBy(p => p.Id).Include(p => p.Sections).ThenInclude(s => s.Sources).AsNoTracking().ToListAsync();
    }
}
