using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace MorningInk.Web;

/// <summary>
/// A reverse proxy on another host, e.g. Nginx Proxy Manager, set as <c>ReverseProxy__Addresses__0</c>. Only requests
/// from its address may say which scheme and client address the browser used (X-Forwarded-Proto, X-Forwarded-For);
/// anyone else in the home network reaching the port directly cannot pretend.
/// </summary>
public static class ReverseProxy
{
    private const string AddressesKey = "ReverseProxy:Addresses";

    public static IServiceCollection AddReverseProxy(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            var addresses = configuration.GetSection(AddressesKey).Get<string[]>() ?? [];
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Only the configured proxies: the default would also trust anything on this host's loopback.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var address in addresses)
            {
                options.KnownProxies.Add(IPAddress.TryParse(address.Trim(), out var ip)
                    ? ip
                    : throw new InvalidOperationException($"ReverseProxy__Addresses needs IP addresses; \"{address}\" is none."));
            }
        });
        return services;
    }

    /// <summary>First in the pipeline, so the scheme and address are right for everything after it.</summary>
    public static WebApplication UseReverseProxy(this WebApplication app)
    {
        app.UseForwardedHeaders();
        return app;
    }
}
