using Microsoft.Extensions.Localization;
using MorningInk.Application.Delivery;

namespace MorningInk.Web;

/// <summary>Delivery problems in the user's language: the reason code picks the words, the English text is the fallback.</summary>
public static class DeliveryTexts
{
    public static string Describe(IStringLocalizer<SharedResource> texts, DeliveryResult.Failure failure) =>
        Describe(texts, failure.Reason, failure.Arguments, failure.Message);

    public static string Describe(IStringLocalizer<SharedResource> texts, string? reason, IReadOnlyList<string> arguments, string fallback)
    {
        if (reason is null)
        {
            return fallback;
        }

        var text = texts[$"Delivery_{reason}", [.. arguments]];
        return text.ResourceNotFound ? fallback : text.Value;
    }
}
