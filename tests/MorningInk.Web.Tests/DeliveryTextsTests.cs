using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MorningInk.Application.Delivery;

namespace MorningInk.Web.Tests;

public sealed class DeliveryTextsTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public void Delivery_problems_come_in_the_users_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            var texts = _app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();

            var text = DeliveryTexts.Describe(texts, new DeliveryResult.Failure("The mail server smtp.example.com could not be reached.", true, "Unreachable", ["smtp.example.com"]));

            Assert.Equal("Der Mailserver smtp.example.com ist nicht erreichbar.", text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Problem_without_a_reason_keeps_its_own_text()
    {
        var texts = _app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        Assert.Equal("Something else", DeliveryTexts.Describe(texts, new DeliveryResult.Failure("Something else", false)));
    }

    public void Dispose() => _app.Dispose();
}
