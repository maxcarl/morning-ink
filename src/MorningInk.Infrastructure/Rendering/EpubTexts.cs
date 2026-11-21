using System.Globalization;
using System.Resources;

namespace MorningInk.Infrastructure.Rendering;

/// <summary>Texts printed in issues, from EpubTexts.resx (English) and EpubTexts.de.resx (German).</summary>
internal sealed class EpubTexts(CultureInfo culture)
{
    private static readonly ResourceManager Resources = new("MorningInk.Infrastructure.Rendering.EpubTexts", typeof(EpubTexts).Assembly);

    public CultureInfo Culture { get; } = culture;

    public string Contents => Get(nameof(Contents));

    public string TitlePage => Get(nameof(TitlePage));

    public string Headlines => Get(nameof(Headlines));

    public string Notes => Get(nameof(Notes));

    public string ReadOriginal => Get(nameof(ReadOriginal));

    public string IssueNumber(int number) => string.Format(Culture, Get(nameof(IssueNumber)), number);

    public string ReadingTime(int minutes) => string.Format(Culture, Get(nameof(ReadingTime)), minutes);

    /// <summary>Texts for <paramref name="language"/>; unknown languages fall back to English.</summary>
    public static EpubTexts For(string language)
    {
        try
        {
            return new EpubTexts(CultureInfo.GetCultureInfo(language));
        }
        catch (CultureNotFoundException)
        {
            return new EpubTexts(CultureInfo.GetCultureInfo("en"));
        }
    }

    private string Get(string name) =>
        Resources.GetString(name, Culture) ?? throw new InvalidOperationException($"Missing text {name}.");
}
