using Microsoft.Extensions.Localization;
using MorningInk.Application.Delivery;

namespace MorningInk.Web;

/// <summary>Delivery problems in the user's language; see <see cref="ReasonTexts"/>.</summary>
public static class DeliveryTexts
{
    public static string Describe(IStringLocalizer<SharedResource> texts, DeliveryResult.Failure failure) =>
        Describe(texts, failure.Reason, failure.Arguments, failure.Message);

    public static string Describe(IStringLocalizer<SharedResource> texts, string? reason, IReadOnlyList<string> arguments, string fallback) =>
        ReasonTexts.Describe(texts, "Delivery", reason, arguments, fallback);
}
