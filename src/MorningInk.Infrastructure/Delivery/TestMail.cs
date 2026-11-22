using MorningInk.Application.Delivery;
using MorningInk.Application.Rendering;
using MorningInk.Application.Sources;

namespace MorningInk.Infrastructure.Delivery;

/// <summary>
/// Sends a tiny test issue through the real mail delivery. An EPUB rather than a plain mail, because Send to Kindle
/// drops mails without an attachment: if this one arrives on the Kindle, real issues will too.
/// </summary>
public sealed class TestMail(IEnumerable<IRenderer> renderers, IDelivery delivery, TimeProvider time)
{
    private const string Title = "Morning Ink";
    private static readonly Uri ProjectUrl = new("https://github.com/maxcarl/morning-ink");

    public async Task<DeliveryResult> SendAsync(string recipient, string language, CancellationToken cancellationToken)
    {
        var renderer = renderers.First(r => r.FileExtension == ".epub");
        var now = time.GetUtcNow();
        var article = new FetchedArticle(null, "It works", ProjectUrl, now, null, null, "<p>This test issue came through your mail settings. Real issues will arrive the same way.</p>");
        var content = new IssueContent(Guid.NewGuid(), Title, 0, now, language, [new IssueSection("Test", [new IssueArticle(article, Title)])], []);

        // A random name in the temp directory, never derived from input.
        var path = Path.Combine(Path.GetTempPath(), $"morning-ink-test-{Guid.NewGuid():N}{renderer.FileExtension}");
        try
        {
            await using (var file = File.Create(path))
            {
                await renderer.RenderAsync(content, file, cancellationToken);
            }

            return await delivery.DeliverAsync(new DeliveryRequest(recipient, $"{Title} Test {now:yyyy-MM-dd HH-mm}", path, renderer.FileExtension, renderer.MediaType), cancellationToken);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
