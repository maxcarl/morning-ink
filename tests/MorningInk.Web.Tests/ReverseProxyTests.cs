using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MorningInk.Web.Tests;

/// <summary>A proxy on another host, e.g. Nginx Proxy Manager: only its own address may tell the browser's scheme and address.</summary>
public sealed class ReverseProxyTests
{
    private const string Proxy = "192.168.1.20";
    private const string OtherComputer = "192.168.1.50";

    [Theory]
    [InlineData(Proxy, "https://news.example.com/opds")]
    [InlineData(OtherComputer, "http://news.example.com/opds")] // reaching the port directly, it cannot pretend
    public async Task Only_the_configured_proxy_may_say_the_browser_used_https(string from, string catalog)
    {
        using var app = new TestApp(new() { ["ReverseProxy:Addresses:0"] = Proxy });
        using var factory = app.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddress())));
        var browser = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await TestApp.SubmitAsync(browser, "/setup", new()
        {
            ["Input.Code"] = factory.Services.GetRequiredService<MorningInk.Infrastructure.Identity.SetupCode>().Generate(),
            ["Input.UserName"] = TestApp.AdminName,
            ["Input.Password"] = TestApp.AdminPassword,
            ["Input.ConfirmPassword"] = TestApp.AdminPassword,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add(RemoteAddress.Header, from);
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Host = "news.example.com";

        var page = await (await browser.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains(catalog, page, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_configured_proxy_is_known()
    {
        using var app = new TestApp(new() { ["ReverseProxy:Addresses:0"] = Proxy });

        var options = app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal([IPAddress.Parse(Proxy)], options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks); // not even this host's loopback
    }

    [Fact]
    public void A_proxy_given_by_name_instead_of_address_stops_the_start()
    {
        using var app = new TestApp(new() { ["ReverseProxy:Addresses:0"] = "npm.lan" });

        Assert.Throws<InvalidOperationException>(() => app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value);
    }

    // The test server knows no client address; this one takes it from a header, as Kestrel would from the connection.
    private sealed class RemoteAddress : IStartupFilter
    {
        public const string Header = "X-Test-Remote-Address";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[Header].FirstOrDefault() ?? OtherComputer);
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
