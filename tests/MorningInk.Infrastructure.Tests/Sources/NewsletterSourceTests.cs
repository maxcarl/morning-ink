using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using MorningInk.Application.Sources;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Sources;
using MorningInk.Infrastructure.Tests.Persistence;

namespace MorningInk.Infrastructure.Tests.Sources;

/// <summary>Newsletters from an IMAP folder, read only: the mailbox looks the same afterwards.</summary>
public sealed class NewsletterSourceTests : IDisposable
{
    private const string User = "reader";
    private const string Password = "imap-password";
    private const string Folder = "Newsletter";

    private readonly TestHost _host = new();
    private readonly FakeImapServer _imap;

    public NewsletterSourceTests() => _imap = new FakeImapServer(User, Password, new()
    {
        ["INBOX"] = [],
        [Folder] =
        [
            Mail("Wochenbrief", "redaktion@brief.example.com", "Der Wochenbrief", html: "<h1>Hallo</h1><p>Die Woche in <b>Kürze</b>.</p><img src=\"https://track.example.com/p.gif\">"),
            Mail("Technik-Post", "post@technik.example.org", "Technik-Post", text: "Erste Zeile\n\nZweiter Absatz <script>x</script>"),
        ],
    });

    [Fact]
    public async Task Mails_of_the_folder_become_articles_and_the_mailbox_stays_untouched()
    {
        await using var services = await StartAsync();
        var source = await AddAsync(services);

        var articles = Assert.IsType<SourceResult.Success>(await FetchAsync(services, source)).Articles;

        Assert.Equal(["Technik-Post", "Wochenbrief"], articles.Select(a => a.Title).Order());
        var brief = articles.Single(a => a.Title == "Wochenbrief");
        Assert.Equal("Der Wochenbrief", brief.Author);
        Assert.Contains("<b>Kürze</b>", brief.Content, StringComparison.Ordinal);
        Assert.Equal("mid", brief.Url.Scheme); // the message id: no web page, but stable for "seen"
        var post = articles.Single(a => a.Title == "Technik-Post");
        Assert.Contains("<p>Zweiter Absatz &lt;script&gt;x&lt;/script&gt;</p>", post.Content, StringComparison.Ordinal); // plain text, escaped

        // Read only: the folder is examined, never selected, and no flag is ever set.
        Assert.Contains(_imap.Commands, c => c.StartsWith("EXAMINE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_imap.Commands, c => c.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || c.Contains("STORE", StringComparison.OrdinalIgnoreCase) || c.Contains("BODY[]", StringComparison.OrdinalIgnoreCase) || c.Contains("EXPUNGE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Only_mails_of_the_chosen_sender_are_taken()
    {
        await using var services = await StartAsync();
        var source = await AddAsync(services, sender: "technik.example.org");

        var articles = Assert.IsType<SourceResult.Success>(await FetchAsync(services, source)).Articles;

        Assert.Equal("Technik-Post", Assert.Single(articles).Title);
    }

    [Theory]
    [InlineData("wrong", Folder, "NewsletterLogin")]
    [InlineData(Password, "Fehlt", "NewsletterFolder")]
    public async Task Adding_checks_login_and_folder_first(string password, string folder, string reason)
    {
        await using var services = await StartAsync();

        var (source, failure) = await AddNewsletterAsync(services, new NewsletterInput("Brief", $"127.0.0.1:{_imap.Port}", User, password, folder, null));

        Assert.Null(source);
        Assert.Equal(reason, failure?.Reason);
    }

    [Fact]
    public async Task Mail_server_in_the_home_network_must_be_allowed_first()
    {
        await using var services = await _host.StartAsync();

        var (_, failure) = await AddNewsletterAsync(services, new NewsletterInput("Brief", $"127.0.0.1:{_imap.Port}", User, Password, Folder, null));

        Assert.Equal(("BlockedAddress", $"127.0.0.1:{_imap.Port}"), (failure?.Reason, failure?.Arguments?[0]));
        Assert.Empty(_imap.Commands); // not even connected
    }

    [Theory]
    [InlineData(993, false, MailKit.Security.SecureSocketOptions.SslOnConnect)]
    [InlineData(143, false, MailKit.Security.SecureSocketOptions.StartTls)]
    [InlineData(1143, false, MailKit.Security.SecureSocketOptions.StartTls)]
    [InlineData(143, true, MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable)] // unencrypted only inside the home network
    public void Password_leaves_the_home_network_only_encrypted(int port, bool homeNetwork, MailKit.Security.SecureSocketOptions expected) =>
        Assert.Equal(expected, NewsletterClient.Security(port, homeNetwork));

    [Fact]
    public async Task Oversized_mails_are_skipped()
    {
        using var big = new FakeImapServer(User, Password, new() { [Folder] = [Mail("Riesig", "a@example.com", "A", text: new string('x', NewsletterClient.MaxMessageBytes + 1))] });
        await using var services = await _host.StartAsync(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{big.Port}" });
        var (source, _) = await AddNewsletterAsync(services, new NewsletterInput("Brief", $"127.0.0.1:{big.Port}", User, Password, Folder, null));

        Assert.Empty(Assert.IsType<SourceResult.Success>(await FetchAsync(services, source!)).Articles);
    }

    public void Dispose()
    {
        _imap.Dispose();
        _host.Dispose();
    }

    private static MimeMessage Mail(string subject, string from, string name, string? html = null, string? text = null)
    {
        var message = new MimeMessage { Subject = subject, Date = TestHost.Now.AddHours(-2), MessageId = $"{Guid.NewGuid():N}@example.com" };
        message.From.Add(new MailboxAddress(name, from));
        message.To.Add(new MailboxAddress("Reader", "reader@example.com"));
        message.Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody();
        return message;
    }

    private Task<ServiceProvider> StartAsync() => _host.StartAsync(new() { ["Http:AllowedHosts:0"] = $"127.0.0.1:{_imap.Port}" });

    private async Task<Source> AddAsync(IServiceProvider services, string? sender = null)
    {
        var (source, failure) = await AddNewsletterAsync(services, new NewsletterInput("Brief", $"127.0.0.1:{_imap.Port}", User, Password, Folder, sender));
        Assert.Null(failure);
        return source!; // ! is safe: no failure means a source
    }

    private static async Task<(Source? Source, SourceResult.Failure? Failure)> AddNewsletterAsync(IServiceProvider services, NewsletterInput input)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
        var user = new User { Name = $"reader-{Guid.NewGuid():N}" };
        db.DomainUsers.Add(user);
        await db.SaveChangesAsync();
        return await scope.ServiceProvider.GetRequiredService<ConnectedSources>().AddNewsletterAsync(user.Id, input, CancellationToken.None);
    }

    private static async Task<SourceResult> FetchAsync(IServiceProvider services, Source source)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetServices<ISource>().Single(s => s.Kind == SourceKind.Newsletter).FetchAsync(source, CancellationToken.None);
    }
}
