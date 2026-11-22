using MorningInk.Application.Processing;
using MorningInk.Application.Sources;
using MorningInk.Domain;

namespace MorningInk.Application.Tests.Processing;

public class InterestsTests
{
    private static readonly Interest Homelab = new() { Topic = "Homelab", Keywords = ["Proxmox", "NAS", "Raspberry Pi"], Weight = InterestWeight.StrongPrefer };
    private static readonly Interest Football = new() { Topic = "Fußball", Keywords = ["Bundesliga", "FC Bayern"], Weight = InterestWeight.Hide };
    private static readonly Interest Celebrities = new() { Topic = "Promis", Keywords = ["Promi"], Weight = InterestWeight.Less };

    [Fact]
    public void Matching_interests_raise_or_lower_an_article()
    {
        Interest[] interests = [Homelab, Celebrities];

        Assert.True(Interests.Score(Article("Neues NAS für zu Hause"), interests) > Interests.Score(Article("Wetter am Wochenende"), interests));
        Assert.True(Interests.Score(Article("Promi heiratet am See"), interests) < Interests.Score(Article("Wetter am Wochenende"), interests));
    }

    [Fact]
    public void Hidden_topics_are_left_out()
    {
        Assert.True(Interests.Hides(Article("Bundesliga: Spitzenspiel am Abend"), [Football]));
        Assert.False(Interests.Hides(Article("Stadtrat tagt"), [Football]));
    }

    [Fact]
    public void Keywords_count_in_the_teaser_too() =>
        Assert.True(Interests.Score(Article("Kernel-Update", "<p>Auch für Proxmox.</p>"), [Homelab]) > 0);

    private static FetchedArticle Article(string title, string? summary = null) =>
        new(null, title, new Uri("https://example.com/a"), null, null, summary, null);
}
