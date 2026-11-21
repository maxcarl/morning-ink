using MorningInk.Infrastructure;

namespace MorningInk.Web;

/// <summary>Composition root: wires the layers together and starts the web host.</summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddInfrastructure(builder.Configuration);

        var app = builder.Build();
        // Before serving requests, so no request ever sees an outdated schema.
        await app.MigrateDatabaseAsync();

        app.MapGet("/", () => "Hello World!");
        app.MapHealthChecks("/health");

        await app.RunAsync();
    }
}
