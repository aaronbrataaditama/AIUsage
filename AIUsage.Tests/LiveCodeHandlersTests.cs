using AIUsage.Bridge.Handlers;
using AIUsage.Tickets;

namespace AIUsage.Tests;

/// <summary>
/// The Live Code merged ticket picker sorts JIRA + ClickUp tickets together by <c>Updated</c>. JIRA's
/// raw REST timestamp carries a site-local offset with no colon (e.g. "+0530"); ClickUp's is UTC "o"
/// format ("…Z"). These two shapes are NOT mutually comparable as plain strings — ordinal comparison
/// can rank a chronologically older JIRA ticket ahead of a newer ClickUp one, and with `.Take(count)`
/// silently drop the actually-most-recent ticket from the merged list.
/// </summary>
public class LiveCodeHandlersTests
{
    private static TicketInfo Ticket(string key, string provider, string? updated, bool isDone = false, int priorityRank = 0) =>
        new(key, provider, "Summary", "Open", "Task", "PROJ", null, "Medium", updated, Description: null, IsDone: isDone, PriorityRank: priorityRank);

    [Fact]
    public void ParseUpdated_handles_jiras_colonless_offset_and_clickups_utc_o_format()
    {
        var jira = LiveCodeHandlers.ParseUpdated("2024-01-15T10:30:00.000+0530");
        var clickUp = LiveCodeHandlers.ParseUpdated("2024-01-15T06:00:00.0000000Z");

        Assert.NotNull(jira);
        Assert.NotNull(clickUp);
        // 10:30+05:30 == 05:00 UTC — chronologically OLDER than ClickUp's 06:00 UTC, even though the
        // raw JIRA string is lexically GREATER ("10..." > "06...").
        Assert.True(jira!.Value.UtcDateTime < clickUp!.Value.UtcDateTime);
    }

    [Fact]
    public void ParseUpdated_returns_null_for_missing_or_unparseable_values()
    {
        Assert.Null(LiveCodeHandlers.ParseUpdated(null));
        Assert.Null(LiveCodeHandlers.ParseUpdated(""));
        Assert.Null(LiveCodeHandlers.ParseUpdated("   "));
        Assert.Null(LiveCodeHandlers.ParseUpdated("not a date"));
    }

    [Fact]
    public void OrderByUpdatedDesc_ranks_by_chronological_instant_not_lexical_string()
    {
        // Reviewer's exact scenario: a JIRA "+0530" timestamp that is chronologically OLDER but
        // lexically GREATER than a ClickUp UTC one must come SECOND (i.e. the merge must not use
        // ordinal string comparison across the two trackers' differing formats).
        var jiraOlderButLexicallyGreater = Ticket("ABC-1", TicketProviderIds.Jira, "2024-01-15T10:30:00.000+0530");
        var clickUpNewer = Ticket("CU-2", TicketProviderIds.ClickUp, "2024-01-15T06:00:00.0000000Z");

        var ordered = LiveCodeHandlers.OrderByUpdatedDesc(new[] { jiraOlderButLexicallyGreater, clickUpNewer }).ToList();

        Assert.Equal("CU-2", ordered[0].Key);
        Assert.Equal("ABC-1", ordered[1].Key);
    }

    [Fact]
    public void MergeForPicker_drops_done_tickets_before_taking_count()
    {
        // A done ticket must never surface in the Live Code picker, even if it's the most
        // recently updated one — it would otherwise outrank an open ticket and, with Take(count),
        // could push the open one off the list entirely.
        var done = Ticket("ABC-1", TicketProviderIds.Jira, "2024-01-15T10:30:00.000+0000", isDone: true);
        var open1 = Ticket("ABC-2", TicketProviderIds.Jira, "2024-01-14T10:30:00.000+0000");
        var open2 = Ticket("ABC-3", TicketProviderIds.Jira, "2024-01-13T10:30:00.000+0000");

        var merged = LiveCodeHandlers.MergeForPicker(new[] { done, open1, open2 }, count: 2);

        Assert.Equal(2, merged.Count);
        Assert.DoesNotContain(merged, t => t.Key == "ABC-1");
        Assert.Equal(["ABC-2", "ABC-3"], merged.Select(t => t.Key).ToList());
    }

    [Fact]
    public void MergeForPicker_ranks_lower_priority_number_first_even_if_older()
    {
        // ClickUp's "In Progress" (rank 0) must outrank its "Planned" (rank 2) even when the
        // Planned ticket was updated more recently — priority beats recency.
        var planned = Ticket("ABC-1", TicketProviderIds.ClickUp, "2024-01-15T00:00:00.0000000Z", priorityRank: 2);
        var inProgress = Ticket("ABC-2", TicketProviderIds.ClickUp, "2024-01-10T00:00:00.0000000Z", priorityRank: 0);

        var merged = LiveCodeHandlers.MergeForPicker(new[] { planned, inProgress }, count: 2);

        Assert.Equal(["ABC-2", "ABC-1"], merged.Select(t => t.Key).ToList());
    }

    [Fact]
    public void MergeForPicker_breaks_ties_within_a_rank_by_recency()
    {
        var older = Ticket("ABC-1", TicketProviderIds.Jira, "2024-01-10T00:00:00.000+0000");
        var newer = Ticket("ABC-2", TicketProviderIds.Jira, "2024-01-14T00:00:00.000+0000");

        var merged = LiveCodeHandlers.MergeForPicker(new[] { older, newer }, count: 2);

        Assert.Equal(["ABC-2", "ABC-1"], merged.Select(t => t.Key).ToList());
    }

    [Fact]
    public void OrderByUpdatedDesc_sorts_null_or_unparseable_updated_last()
    {
        var withDate = Ticket("ABC-1", TicketProviderIds.Jira, "2024-01-15T10:30:00.000+0530");
        var noDate = Ticket("ABC-2", TicketProviderIds.Jira, null);
        var badDate = Ticket("ABC-3", TicketProviderIds.Jira, "not a date");

        var ordered = LiveCodeHandlers.OrderByUpdatedDesc(new[] { noDate, withDate, badDate }).ToList();

        Assert.Equal("ABC-1", ordered[0].Key);
        Assert.Contains(ordered[1].Key, new[] { "ABC-2", "ABC-3" });
        Assert.Contains(ordered[2].Key, new[] { "ABC-2", "ABC-3" });
    }
}
