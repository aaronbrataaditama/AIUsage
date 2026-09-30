using AIUsage.ClickUp;

namespace AIUsage.Tickets;

/// <summary>Wraps <see cref="ClickUpClient"/> behind <see cref="ITicketProvider"/>.</summary>
public sealed class ClickUpTicketProvider(ClickUpClient client) : ITicketProvider
{
    /// <summary>Hard cap on pages walked by <see cref="AssignedAsync"/>, regardless of <c>max</c> —
    /// a runaway workspace shouldn't turn one bridge call into an unbounded fetch loop.</summary>
    private const int MaxPages = 3;

    /// <summary>Fixed status-name priority for the Live Code picker, most workable first. A task
    /// whose status isn't one of these is excluded from the picker entirely (not just
    /// deprioritized) — Testing/Documentation/Done/Closed/etc. clutter the "what to start next"
    /// list. Matched case-insensitively against ClickUp's own status name.</summary>
    private static readonly string[] StatusPriority = ["in progress", "pr initiated", "planned", "open"];

    public string Id => TicketProviderIds.ClickUp;
    public string DisplayName => "ClickUp";
    public string ItemNoun => "task";

    public Task<TicketInfo?> FetchAsync(string key) => client.FetchTaskAsync(key);

    public async Task<List<TicketInfo>> AssignedAsync(int max)
    {
        var all = new List<TicketInfo>();
        for (var page = 0; page < MaxPages; page++)
        {
            var (tasks, isLast) = await client.AssignedAsync(page);
            all.AddRange(tasks);
            if (isLast || all.Count >= max) break;
        }
        return RankAndFilter(all);
    }

    /// <summary>Keep only tasks whose status matches <see cref="StatusPriority"/>, tagging each
    /// with its tier (<see cref="TicketInfo.PriorityRank"/>, 0 = highest) so the merged Live Code
    /// picker ranks by status ahead of recency. Orders by rank, then by ClickUp's own "updated"
    /// string as the tiebreak (plain-text comparable — see <see cref="AssignedAsync"/>'s remark on
    /// ClickUp's order_by not being guaranteed).</summary>
    internal static List<TicketInfo> RankAndFilter(IEnumerable<TicketInfo> tasks) =>
        tasks
            .Select(t => (Ticket: t, Rank: Array.IndexOf(StatusPriority, (t.Status ?? "").Trim().ToLowerInvariant())))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenByDescending(x => x.Ticket.Updated, StringComparer.Ordinal)
            .Select(x => x.Ticket with { PriorityRank = x.Rank })
            .ToList();
}
