using MorningInk.Infrastructure;

namespace MorningInk.Web;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddInfrastructure(builder.Configuration);

        var app = builder.Build();
        await app.MigrateDatabaseAsync();

        app.MapGet("/", () => "Hello World!");
        app.MapHealthChecks("/health");

        await app.RunAsync();
    }
}
