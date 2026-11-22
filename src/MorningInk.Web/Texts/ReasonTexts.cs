using Microsoft.Extensions.Localization;

namespace MorningInk.Web;

/// <summary>Failures in the user's language: the reason code picks the words, the English text is the fallback.</summary>
public static class ReasonTexts
{
    /// <param name="area">Prefix of the resource keys, e.g. "Delivery" for "Delivery_Unreachable".</param>
    public static string Describe(IStringLocalizer<SharedResource> texts, string area, string? reason, IReadOnlyList<string> arguments, string fallback)
    {
        if (reason is null)
        {
            return fallback;
        }

        var text = texts[$"{area}_{reason}", [.. arguments]];
        return text.ResourceNotFound ? fallback : text.Value;
    }
}
