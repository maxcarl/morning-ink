using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Wikipedia;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Wikipedia;

/// <summary>"On this day" against saved responses on a local server: German and English exist, Polish does not.</summary>
public sealed class WikipediaOnThisDayTests : IDisposable
{
    private static readonly DateOnly Day = new(2026, 11, 24);

    private readonly LocalServer _server = new(async context =>
    {
        var file = context.Request.Url!.AbsolutePath switch // ! is safe: a received request always has a URL
        {
            "/feed/de/onthisday/selected/11/24" => "onthisday-de.json",
            "/feed/en/onthisday/selected/11/24" => "onthisday-en.json",
            _ => null,
        };
        if (file is null)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Wikipedia", file)));
    });

    [Fact]
    public async Task Five_plain_events_in_the_countrys_language_come_oldest_first()
    {
        var events = await OnThisDay().EventsAsync(Day, "AT", CancellationToken.None);

        // The empty and the overlong entries are dropped, the sixth valid one is beyond the limit.
        Assert.Equal([1850, 1876, 1911, 1991, 2001], events.Select(e => e.Year));
        Assert.Contains(events, e => e.Text == "Ein Museum öffnet in Beispielhausen.");
    }

    [Theory]
    [InlineData("PL")] // a Wikipedia without the list
    [InlineData(null)]
    public async Task Without_a_list_in_the_countrys_language_it_comes_in_English(string? country)
    {
        var events = await OnThisDay().EventsAsync(Day, country, CancellationToken.None);

        Assert.Equal("A library opens in Sampletown.", Assert.Single(events).Text);
    }

    [Fact]
    public async Task An_unavailable_service_gives_nothing()
    {
        Assert.Empty(await OnThisDay("/missing").EventsAsync(Day, "DE", CancellationToken.None));
    }

    public void Dispose() => _server.Dispose();

    private IOnThisDay OnThisDay(string path = "/feed") => new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = Path.GetTempPath(),
                ["Http:AllowedHosts:0"] = $"127.0.0.1:{_server.Port}",
                ["Wikipedia:FeedUrl"] = _server.Url(path).ToString(),
            })
            .Build())
        .BuildServiceProvider()
        .GetRequiredService<IOnThisDay>();
}
