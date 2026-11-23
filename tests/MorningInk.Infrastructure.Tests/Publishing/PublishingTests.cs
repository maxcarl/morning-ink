using System.IO.Compression;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using MorningInk.Application.Delivery;
using MorningInk.Application.Processing;
using MorningInk.Application.Publishing;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;
using MorningInk.Application.Weather;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Tests.Delivery;
using MorningInk.Infrastructure.Tests.Http;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Publishing;

/// <summary>End to end with real parts: feed source, database, EPUB renderer and SMTP delivery, all on loopback.</summary>
public sealed class PublishingTests : IDisposable
{
    private static readonly DateTimeOffset Slot = new(2026, 11, 21, 6, 0, 0, TimeSpan.FromHours(1));

    private readonly TestHost _host = new();
    private readonly FakeSmtpServer _smtp = new();
    private readonly LocalServer _feeds = new(async context =>
    {
        if (context.Request.Url!.AbsolutePath == "/down") // ! is safe: a received request always has a URL
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            return;
        }

        var feed = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Feeds", "edge-cases.xml"));
        await context.Response.OutputStream.WriteAsync(feed);
    });

    [Fact]
    public async Task Failed_source_still_brings_the_issue_with_a_note()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services);

        var result = await PublishAsync(services, profileId, Slot);

        var delivered = Assert.IsType<PublishResult.Delivered>(result);
        Assert.Equal(3, delivered.Articles);
        Assert.Equal("Ausfall", Assert.Single(delivered.UnavailableSources).SourceName);

        var mail = Assert.Single(_smtp.Messages);
        Assert.Equal("Morning Ink 2026-11-21", mail.Subject);
        var attachment = Assert.IsType<MimePart>(Assert.Single(mail.Attachments));
        Assert.Equal("Morning Ink 2026-11-21.epub", attachment.FileName);
        using var epub = new MemoryStream();
        await attachment.Content!.DecodeToAsync(epub); // ! is safe: the delivery always sets content

        // CI runs epubcheck on this book too: it went the whole way through source, pipeline and renderer.
        if (Environment.GetEnvironmentVariable("MORNINGINK_EPUB_OUTPUT") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output, "published-issue.epub"), epub.ToArray());
        }

        using var zip = new ZipArchive(epub);
        using var titlePage = new StreamReader(zip.GetEntry("OEBPS/title.xhtml")!.Open()); // ! is safe: every book has a title page
        Assert.Contains("Ausfall war heute nicht erreichbar", await titlePage.ReadToEndAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_same_slot_is_never_sent_twice_even_in_another_time_zone()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services);
        await PublishAsync(services, profileId, Slot);

        var again = await PublishAsync(services, profileId, Slot.ToUniversalTime());

        Assert.IsType<PublishResult.AlreadyDelivered>(again);
        Assert.Single(_smtp.Messages);
    }

    [Fact]
    public async Task Next_day_brings_nothing_already_delivered()
    {
        await using var services = await StartAsync();
        var profileId = await CreateProfileAsync(services);
        await PublishAsync(services, profileId, Slot);

        var nextDay = await PublishAsync(services, profileId, Slot.AddDays(1));

        Assert.IsType<PublishResult.NoNewArticles>(nextDay);
        Assert.Single(_smtp.Messages);
    }

    public void Dispose()
    {
        _feeds.Dispose();
        _smtp.Dispose();
        _host.Dispose();
    }

    private Task<ServiceProvider> StartAsync() => _host.StartAsync(new Dictionary<string, string?>
    {
        ["Http:AllowedHosts:0"] = $"127.0.0.1:{_feeds.Port}",
        ["Smtp:Host"] = "127.0.0.1",
        ["Smtp:Port"] = _smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Smtp:Security"] = "None",
        ["Smtp:FromAddress"] = "morning-ink@example.com",
    });

    private async Task<Guid> CreateProfileAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = "max", Language = "de" };

        // The edge-case feed links to example.com; full text stays off so the test never leaves the machine.
        var working = new Source { UserId = user.Id, Name = "Test-Feed", Url = _feeds.Url("/feed").ToString(), FetchFullText = false, MaxAge = TimeSpan.FromDays(3650) };
        var broken = new Source { UserId = user.Id, Name = "Ausfall", Url = _feeds.Url("/down").ToString() };
        var profile = new Profile { UserId = user.Id, Title = "Morning Ink", Recipient = "max_abc@kindle.com" };
        profile.Sections.Add(new Section { Name = "Nachrichten", Position = 1, Sources = { working, broken } });
        db.AddRange(user, working, broken, profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    // Wired like the app does it (Web's AddApplication), one scope per run like a scheduled job.
    private static async Task<PublishResult> PublishAsync(IServiceProvider services, Guid profileId, DateTimeOffset slot)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var time = provider.GetRequiredService<TimeProvider>();
        var store = provider.GetRequiredService<IPublishingStore>();
        var sources = provider.GetServices<ISource>().ToList();
        var seen = provider.GetRequiredService<ISeenArticles>();
        var files = provider.GetRequiredService<IIssueFiles>();
        var publisher = new IssuePublisher(
            store,
            new IssueBuilder(sources, seen, provider.GetServices<IProcessor>(), new IssueAiSteps(provider.GetRequiredService<IInterestMatcher>(), provider.GetRequiredService<IHeadlineEditor>(), provider.GetRequiredService<IIssueOverview>(), time), provider.GetRequiredService<IGeocoder>(), provider.GetRequiredService<IWeatherForecast>(), time),
            new IssueRelease(store, sources, seen, provider.GetRequiredService<IDelivery>(), files, provider.GetRequiredService<MorningInk.Application.Notifications.INotifier>(), time),
            provider.GetServices<IRenderer>(),
            provider.GetRequiredService<IFeedbackLinks>(),
            files,
            Locks,
            time);
        return await publisher.PublishAsync(profileId, slot, CancellationToken.None);
    }

    private static readonly ProfileLocks Locks = new();
}
