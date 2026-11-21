using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Infrastructure.Identity;

namespace MorningInk.Web.Tests;

/// <summary>The real app in memory, on an empty data directory of its own.</summary>
public sealed partial class TestApp : WebApplicationFactory<Program>
{
    public const string AdminName = "max";
    public const string AdminPassword = "correct horse battery staple";

    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("morning-ink-web-").FullName;

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

    /// <summary>Loads a form page and posts it with its hidden fields (antiforgery token, form name) plus the given values.</summary>
    /// <param name="action">Where the form posts to, if not back to the page itself, e.g. "/logout".</param>
    public static async Task<HttpResponseMessage> SubmitAsync(HttpClient browser, string url, Dictionary<string, string> values, string? action = null)
    {
        var page = await browser.GetStringAsync(url);
        var fields = HiddenInput().Matches(page).ToDictionary(m => m.Groups["name"].Value, m => WebUtility.HtmlDecode(m.Groups["value"].Value));
        foreach (var (name, value) in values)
        {
            fields[name] = value;
        }

        return await browser.PostAsync(action ?? url, new FormUrlEncodedContent(fields));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Storage:DataDirectory", _dataDirectory);
        builder.UseEnvironment("Testing");
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

    [GeneratedRegex("""<input type="hidden" name="(?<name>[^"]+)" value="(?<value>[^"]*)""")]
    private static partial Regex HiddenInput();
}
