using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MorningInk.Domain;
using MorningInk.Infrastructure;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Infrastructure.Scheduling;

namespace MorningInk.Web.Tests;

/// <summary>A missing text shows its key on the page, e.g. "Nav_System" in the navigation; these find it first.</summary>
public sealed partial class UiTextsTests : IDisposable
{
    private readonly TestApp _app = new();

    public static TheoryData<string> Languages => ["en", "de"];

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_text_the_pages_name_exists(string language) =>
        Assert.Empty(Missing(language, LiteralKeys()));

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_text_made_from_a_value_exists(string language) =>
        Assert.Empty(Missing(language, [
            .. Enum.GetNames<IssueStatus>().Select(n => $"Status_{n}"),
            .. Enum.GetNames<JobKind>().Select(n => $"JobKind_{n}"),
            .. Enum.GetNames<StorageArea>().Select(n => $"StorageArea_{n}"),
            .. Enum.GetNames<RestoreProblem>().Select(n => $"RestoreProblem_{n}"),
        ]));

    public void Dispose() => _app.Dispose();

    private List<string> Missing(string language, IEnumerable<string> keys)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            // English is the neutral resource file, German its own culture.
            CultureInfo.CurrentUICulture = language == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language);
            var texts = _app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();

            // Each culture on its own: a key missing in German would otherwise fall back to English unnoticed.
            var own = texts.GetAllStrings(includeParentCultures: false).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
            return keys.Where(k => !own.Contains(k)).Distinct().Order(StringComparer.Ordinal).ToList();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // T["Key"] in the Web project's pages and code; keys built at run time are covered by the other test.
    private static IEnumerable<string> LiteralKeys()
    {
        var web = Path.Combine(RepositoryRoot(), "src", "MorningInk.Web");
        return Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal) || f.EndsWith(".cs", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => LiteralKey().Matches(File.ReadAllText(f)).Select(m => m.Groups["key"].Value));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MorningInk.slnx")) && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The tests run outside the repository.");
    }

    [GeneratedRegex("T\\[\"(?<key>[A-Za-z_]+)\"")]
    private static partial Regex LiteralKey();
}
