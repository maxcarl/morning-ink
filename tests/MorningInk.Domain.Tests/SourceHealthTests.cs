namespace MorningInk.Domain.Tests;

public sealed class SourceHealthTests
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 23, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_failure_is_noted_but_only_days_of_failures_are_a_warning()
    {
        var source = Feed();

        source.RecordFailure(Monday, "heise.de could not be reached.", "Unreachable", ["heise.de"]);
        source.RecordFailure(Monday.AddDays(1), "heise.de could not be reached.", "Unreachable", ["heise.de"]);

        Assert.Equal(new SourceHealth.Failing(Monday, Warning: false), source.Health(Monday.AddDays(1)));
        Assert.Equal(new SourceHealth.Failing(Monday, Warning: true), source.Health(Monday.AddDays(2)));
        Assert.Equal(("Unreachable", "heise.de"), (source.LastProblemReason, source.LastProblemArguments[0]));
    }

    [Fact]
    public void A_success_ends_the_failure()
    {
        var source = Feed();
        source.RecordFailure(Monday, "down", null, []);

        source.RecordSuccess(Monday.AddDays(3), articles: 5);

        Assert.Equal(new SourceHealth.Fine(), source.Health(Monday.AddDays(3)));
        Assert.Null(source.LastProblem);
    }

    [Fact]
    public void A_source_that_brings_nothing_for_a_week_is_a_warning()
    {
        var source = Feed();

        source.RecordSuccess(Monday, articles: 0);
        source.RecordSuccess(Monday.AddDays(6), articles: 0);

        Assert.Equal(new SourceHealth.Empty(Monday, Warning: false), source.Health(Monday.AddDays(6)));
        Assert.Equal(new SourceHealth.Empty(Monday, Warning: true), source.Health(Monday.AddDays(7)));

        source.RecordSuccess(Monday.AddDays(8), articles: 1);
        Assert.Equal(new SourceHealth.Fine(), source.Health(Monday.AddDays(8)));
    }

    private static Source Feed() => new() { UserId = Guid.NewGuid(), Name = "heise", Url = "https://heise.de/rss" };
}
