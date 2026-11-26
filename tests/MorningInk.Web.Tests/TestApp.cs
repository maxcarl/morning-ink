using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Identity;
using MorningInk.Infrastructure.Persistence;
using Quartz;

namespace MorningInk.Web.Tests;

/// <summary>The real app in memory, on an empty data directory of its own.</summary>
public sealed partial class TestApp : WebApplicationFactory<Program>
{
    public const string AdminName = "alex";
    public const string AdminPassword = "correct horse battery staple";

    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("morning-ink-web-").FullName;
    private readonly Dictionary<string, string> _settings;
    private readonly Action<IServiceCollection>? _services;

    /// <param name="settings">Extra configuration, e.g. allowed hosts for a local test server.</param>
    /// <param name="services">Replaced registrations, e.g. the running version.</param>
    public TestApp(Dictionary<string, string>? settings = null, Action<IServiceCollection>? services = null)
    {
        _settings = settings ?? [];
        _services = services;
    }

    /// <summary>A client that keeps cookies and shows redirects instead of following them.</summary>
    public HttpClient Browser(string? language = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        if (language is not null)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        }

        return client;
    }

    /// <summary>A fresh setup code; the one from the log is replaced.</summary>
    public string SetupCode() => Services.GetRequiredService<SetupCode>().Generate();

    /// <summary>Runs the setup with the right code, so the client is signed in as admin.</summary>
    public async Task<HttpClient> SignedInAdminAsync()
    {
        var browser = Browser();
        var response = await SubmitAsync(browser, "/setup", new()
        {
            ["Input.Code"] = SetupCode(),
            ["Input.UserName"] = AdminName,
            ["Input.Password"] = AdminPassword,
            ["Input.ConfirmPassword"] = AdminPassword,
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return browser;
    }

    /// <summary>Where a redirect goes: the path for this site, the full URL for any other site.</summary>
    public static string? RedirectTarget(HttpResponseMessage response) => response.Headers.Location switch
    {
        null => null,
        { IsAbsoluteUri: false } relative => relative.OriginalString,
        { Host: "localhost" } local => local.PathAndQuery,
        var foreign => foreign.AbsoluteUri,
    };

    /// <summary>Creates a user who is not an admin and returns a client signed in as them.</summary>
    public async Task<HttpClient> SignedInUserAsync(string name)
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var user = new User { Name = name };
            db.DomainUsers.Add(user);
            await db.SaveChangesAsync();
            var created = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(new AppUser { Id = user.Id, UserName = name }, AdminPassword);
            Assert.True(created.Succeeded);
        }

        var browser = Browser();
        await SubmitAsync(browser, "/login", new() { ["Input.UserName"] = name, ["Input.Password"] = AdminPassword });
        return browser;
    }

    /// <summary>Loads a form page and posts it with its hidden fields (antiforgery token, form name) plus the given values.</summary>
    /// <param name="action">Where the form posts to, if not back to the page itself, e.g. "/logout".</param>
    /// <param name="form">On pages with several forms: the form name (Blazor's "_handler"), and optionally text the form must contain.</param>
    /// <param name="sourceIds">Ticked checkboxes named "SourceIds", sent as a repeated field like a browser does.</param>
    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient browser, string url, Dictionary<string, string> values, string? action = null, (string Name, string? Containing)? form = null, IEnumerable<Guid>? sourceIds = null)
    {
        var page = await browser.GetStringAsync(url);
        return await SubmitAsync(browser, url, page, values, action, form, sourceIds);
    }

    /// <summary>Like the other overload, but on a page already loaded, e.g. the answer to the previous POST.</summary>
    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient browser, string url, string page, Dictionary<string, string> values, string? action = null, (string Name, string? Containing)? form = null, IEnumerable<Guid>? sourceIds = null)
    {
        if (form is { } wanted)
        {
            page = Forms().Matches(page).Select(m => m.Value).First(f =>
                f.Contains($"name=\"_handler\" value=\"{wanted.Name}", StringComparison.Ordinal)
                && (wanted.Containing is null || f.Contains(wanted.Containing, StringComparison.Ordinal)));
        }

        // Like a browser: hidden fields and the prefilled values of text fields go along.
        var fields = HiddenInput().Matches(page).Concat(PrefilledInput().Matches(page))
            .GroupBy(m => m.Groups["name"].Value)
            .ToDictionary(g => g.Key, g => WebUtility.HtmlDecode(g.First().Groups["value"].Value));
        foreach (var (name, value) in values)
        {
            fields[name] = value;
        }

        var body = fields.Concat((sourceIds ?? []).Select(id => KeyValuePair.Create("SourceIds", id.ToString())));
        return await browser.PostAsync(action ?? url, new FormUrlEncodedContent(body));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:DataDirectory", _dataDirectory);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.UseEnvironment("Testing");

        // Quartz keeps schedulers process-wide by name: with the default name, test apps running side by side would
        // share one, and the first to stop would take the others' plans with it.
        builder.ConfigureServices(services => services.Configure<QuartzOptions>(options => options.Properties["quartz.scheduler.instanceName"] = $"test-{Guid.NewGuid():N}"));
        if (_services is not null)
        {
            builder.ConfigureServices(_services);
        }
    }

    // Called more than once (Dispose and DisposeAsync), so the directory may already be gone.
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    [GeneratedRegex("""<input id="[^"]*" name="(?<name>[^"]+)" class="input" value="(?<value>[^"]*)""")]
    private static partial Regex PrefilledInput();

    [GeneratedRegex("<form[^>]*>.*?</form>", RegexOptions.Singleline)]
    private static partial Regex Forms();

    [GeneratedRegex("""<input type="hidden" name="(?<name>[^"]+)" value="(?<value>[^"]*)""")]
    private static partial Regex HiddenInput();
}
