using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MorningInk.Application.Delivery;
using MorningInk.Domain;

namespace MorningInk.Web.Tests;

public sealed class ReasonTextsTests : IDisposable
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

    [Theory]
    [InlineData("de", "heise: heise.de ist nicht erreichbar.")]
    [InlineData("en", "heise: heise.de could not be reached.")]
    public void Source_problems_from_the_log_come_in_the_users_language(string language, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var texts = _app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();
            var entry = new IssueLogEntry { Event = "SourceFailed", Arguments = ["heise", "heise.de could not be reached.", "Unreachable", "heise.de"] };

            Assert.Equal(expected, SourceTexts.Problem(texts, entry));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // Logs written before the codes existed hold only source and English text.
    [Fact]
    public void Source_problem_without_a_code_keeps_its_own_text()
    {
        var texts = _app.Services.GetRequiredService<IStringLocalizer<SharedResource>>();
        var entry = new IssueLogEntry { Event = "SourceFailed", Arguments = ["heise", "Something odd"] };

        Assert.Equal("heise: Something odd", SourceTexts.Problem(texts, entry));
    }

    public void Dispose() => _app.Dispose();
}
