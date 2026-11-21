using System.Security.Cryptography;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Processing;
using MorningInk.Infrastructure.Tests.Http;

namespace MorningInk.Infrastructure.Tests.Processing;

public sealed class ImageProcessorTests : IDisposable
{
    private static readonly byte[] Photo = TestImages.Png(1200, 800);
    private static readonly byte[] Icon = TestImages.Png(24, 24);

    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("morning-ink-").FullName;

    [Fact]
    public async Task Images_are_stored_under_their_hash_and_icons_are_removed()
    {
        using var server = ImageServer();
        var html = $"<p>Text</p><img src=\"{server.Url("/photo.png")}\" alt=\"Foto\"><img src=\"{server.Url("/icon.png")}\"><img src=\"{server.Url("/missing.png")}\">";

        var content = await ProcessAsync(server, html);

        var images = new HtmlParser().ParseDocument(content).Images.ToList();
        var src = Assert.Single(images).GetAttribute("src")!; // ! is safe: the processor always sets src
        Assert.Matches("^images/[0-9a-f]{64}\\.jpg$", src);
        var file = Path.Combine(_dataDirectory, src);
        Assert.Equal(Path.GetFileNameWithoutExtension(src), Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file))));
        Assert.Equal("Foto", images[0].GetAttribute("alt"));
    }

    [Fact]
    public async Task Reusing_a_stored_image_keeps_it_from_the_cleanup()
    {
        using var server = ImageServer();
        var first = await ProcessAsync(server, $"<img src=\"{server.Url("/photo.png")}\">");
        var file = Path.Combine(_dataDirectory, new HtmlParser().ParseDocument(first).Images.Single().GetAttribute("src")!); // ! is safe: the processor always sets src
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));

        await ProcessAsync(server, $"<img src=\"{server.Url("/photo.png")}\">");

        Assert.True(File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddDays(-1));
    }

    [Fact]
    public async Task At_most_max_images_per_article_are_kept()
    {
        using var server = ImageServer();
        var html = string.Concat(Enumerable.Range(0, 12).Select(i => $"<img src=\"{server.Url($"/photo.png?{i}")}\">"));

        var content = await ProcessAsync(server, html);

        Assert.Equal(10, new HtmlParser().ParseDocument(content).Images.Length);
    }

    [Fact]
    public void Processors_run_full_text_then_sanitizing_then_images()
    {
        using var services = BuildServices(port: 0);

        Assert.Equal(
            [typeof(FullTextProcessor), typeof(SanitizingProcessor), typeof(ImageProcessor)],
            services.GetServices<IProcessor>().Select(p => p.GetType()));
    }

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    private static LocalServer ImageServer() => new(async context =>
    {
        var path = context.Request.Url!.AbsolutePath; // ! is safe: a received request always has a URL
        if (path == "/missing.png")
        {
            context.Response.StatusCode = 404;
            return;
        }

        context.Response.ContentType = "image/png";
        await context.Response.OutputStream.WriteAsync(path == "/icon.png" ? Icon : Photo);
    });

    private async Task<string> ProcessAsync(LocalServer server, string html)
    {
        using var services = BuildServices(server.Port);
        var images = services.GetServices<IProcessor>().OfType<ImageProcessor>().Single();
        var article = new FetchedArticle(null, "Title", server.Url("/article"), null, null, null, html);
        var source = new Source { UserId = Guid.NewGuid(), Name = "n", Url = "https://example.com" };
        return (await images.ProcessAsync(article, source, CancellationToken.None)).Content!; // ! is safe: content was given
    }

    private ServiceProvider BuildServices(int port) => new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = _dataDirectory,
                ["Http:AllowedHosts:0"] = $"127.0.0.1:{port}",
            })
            .Build())
        .BuildServiceProvider();
}
