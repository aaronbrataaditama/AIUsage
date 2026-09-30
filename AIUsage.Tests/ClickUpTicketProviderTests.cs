using AIUsage.Tickets;

namespace AIUsage.Tests;

/// <summary>
/// The Live Code picker should only ever show a ClickUp task that's actively workable — "In
/// Progress" first, then "PR Initiated", then "Planned", then "Open" — and hide every other status
/// (Testing, Documentation, Done, Closed, …) entirely, not just deprioritize it.
/// </summary>
public class ClickUpTicketProviderTests
{
    private static TicketInfo Task(string key, string status, string updated) =>
        new(key, TicketProviderIds.ClickUp, "Summary", status, "Task", "PROJ", null, "Medium", updated);

    [Theory]
    [InlineData("In Progress", 0)]
    [InlineData("PR Initiated", 1)]
    [InlineData("Planned", 2)]
    [InlineData("Open", 3)]
    [InlineData("in progress", 0)] // case-insensitive
    public void RankAndFilter_assigns_the_fixed_priority_rank(string status, int expectedRank)
    {
        var result = ClickUpTicketProvider.RankAndFilter([Task("CU-1", status, "2026-01-01T00:00:00.0000000Z")]);
        Assert.Equal(expectedRank, Assert.Single(result).PriorityRank);
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Documentation")]
    [InlineData("Done")]
    [InlineData("Closed")]
    [InlineData("")]
    public void RankAndFilter_drops_any_other_status(string status) =>
        Assert.Empty(ClickUpTicketProvider.RankAndFilter([Task("CU-1", status, "2026-01-01T00:00:00.0000000Z")]));

    [Fact]
    public void RankAndFilter_orders_by_rank_then_by_recency_within_a_rank()
    {
        var olderOpen = Task("CU-1", "Open", "2026-01-01T00:00:00.0000000Z");
        var newerOpen = Task("CU-2", "Open", "2026-01-05T00:00:00.0000000Z");
        var planned = Task("CU-3", "Planned", "2026-01-10T00:00:00.0000000Z"); // newest overall, but lower rank number wins
        var inProgress = Task("CU-4", "In Progress", "2025-12-01T00:00:00.0000000Z"); // oldest overall, but rank 0

        var result = ClickUpTicketProvider.RankAndFilter([olderOpen, newerOpen, planned, inProgress]);

        Assert.Equal(["CU-4", "CU-3", "CU-2", "CU-1"], result.Select(t => t.Key).ToList());
    }
}
