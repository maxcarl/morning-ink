using MorningInk.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
await app.MigrateDatabaseAsync();

app.MapGet("/", () => "Hello World!");
app.MapHealthChecks("/health");

await app.RunAsync();
