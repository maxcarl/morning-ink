using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MorningInk.Domain;
using MorningInk.Infrastructure.Persistence;
using MorningInk.Web.Components.Pages;

namespace MorningInk.Web.Tests;

public sealed class StatisticsPageTests : IDisposable
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Sources_are_listed_with_their_share_of_the_issues()
    {
        using var pages = await Pages.AsAdminAsync(_app);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MorningInkDbContext>();
            var userId = await db.DomainUsers.Where(u => u.Name == TestApp.AdminName).Select(u => u.Id).SingleAsync();
            db.Sources.Add(new Source { UserId = userId, Name = "heise", Url = "https://heise.de/rss" });
            await db.SaveChangesAsync();
        }

        var page = pages.Open<Statistics>("heise");

        Assert.Contains("The last 30 days", page.Markup, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"row\">heise</th>", page.Markup, StringComparison.Ordinal);
    }

    public void Dispose() => _app.Dispose();
}
