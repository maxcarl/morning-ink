using MorningInk.Infrastructure;

namespace MorningInk.Web;

/// <summary>Composition root: wires the layers together and starts the web host.</summary>
/// <remarks>A sealed class rather than a static one, so integration tests can start the app through it.</remarks>
public sealed class Program
{
    private Program()
    {
    }

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddWebUi();

        var app = builder.Build();

        // Before serving requests, so no request ever sees an outdated schema.
        await app.MigrateDatabaseAsync();
        app.UseWebUi();

        await app.RunAsync();
    }
}
