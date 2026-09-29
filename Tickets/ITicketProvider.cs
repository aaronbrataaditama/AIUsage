namespace AIUsage.Tickets;

/// <summary>
/// One tracker's read-only surface, uniform across JIRA and ClickUp so callers (bridge handlers,
/// Live Code) don't branch on which tracker owns a key. See <see cref="TicketProviders"/> for
/// routing a key to the right instance.
/// </summary>
public interface ITicketProvider
{
    /// <summary>One of <see cref="TicketProviderIds"/>.</summary>
    string Id { get; }

    /// <summary>"JIRA" | "ClickUp" — a closed set; also used verbatim in the Live Code kickoff prompt
    /// ("Use the &lt;agent&gt; agent to work on &lt;DisplayName&gt; &lt;key&gt;", etc).</summary>
    string DisplayName { get; }

    /// <summary>"ticket" | "task" — the noun this tracker's items are called.</summary>
    string ItemNoun { get; }

    /// <summary>Fetch one item by key. Null for a dead/unknown key.</summary>
    Task<TicketInfo?> FetchAsync(string key);

    /// <summary>Most-recently-updated items assigned to the current user, newest first, oversized
    /// for the caller to filter (e.g. drop finished statuses) before taking what it needs.</summary>
    Task<List<TicketInfo>> AssignedAsync(int max);
}
