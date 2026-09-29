using AIUsage.ClickUp;

namespace AIUsage.Tickets;

/// <summary>Wraps <see cref="ClickUpClient"/> behind <see cref="ITicketProvider"/>.</summary>
public sealed class ClickUpTicketProvider(ClickUpClient client) : ITicketProvider
{
    /// <summary>Hard cap on pages walked by <see cref="AssignedAsync"/>, regardless of <c>max</c> —
    /// a runaway workspace shouldn't turn one bridge call into an unbounded fetch loop.</summary>
    private const int MaxPages = 3;

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
        // ClickUp's order_by=updated direction isn't guaranteed, so sort client-side.
        return all.OrderByDescending(t => t.Updated, StringComparer.Ordinal).ToList();
    }
}
