#:project ../src/MorningInk.Infrastructure/MorningInk.Infrastructure.csproj
#:property PublishAot=false

// Sends an EPUB through Morning Ink's mail delivery, for the manual Kindle check (docs/kindle-checkliste.md).
// SMTP settings come from environment variables, so passwords never land in the shell history or a file:
//
//   export Smtp__Host=smtp.example.com Smtp__Username=me@example.com Smtp__FromAddress=me@example.com
//   read -rs Smtp__Password && export Smtp__Password
//   dotnet run tools/send-test-issue.cs -- you_abc@kindle.com path/to/issue.epub "Morning Ink 2026-11-21"
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Delivery;
using MorningInk.Infrastructure;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: dotnet run tools/send-test-issue.cs -- <recipient> <file.epub> <title>");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:DataDirectory"] = Path.GetTempPath() })
    .AddEnvironmentVariables()
    .Build();
using var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration).BuildServiceProvider();

var result = await services.GetRequiredService<IDelivery>().DeliverAsync(
    new DeliveryRequest(args[0], args[2], args[1], ".epub", "application/epub+zip"),
    CancellationToken.None);
Console.WriteLine(result switch
{
    DeliveryResult.Delivered => "Sent. Check the Kindle in a few minutes.",
    DeliveryResult.Failure failure => $"Not sent{(failure.Retryable ? " (temporary)" : string.Empty)}: {failure.Message}",
    _ => result.ToString(),
});
return result is DeliveryResult.Delivered ? 0 : 1;
