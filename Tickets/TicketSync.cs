using AIUsage.Data;
using AIUsage.Data.Repositories;

namespace AIUsage.Tickets;

/// <summary>Provider-neutral replacement for the old JIRA-only `Jira.JiraSync`.</summary>
public static class TicketSync
{
    /// <summary>
    /// Fire-and-forget enrichment of a newly referenced ticket. Quietly does nothing when no
    /// tracker owns the key, or it isn't configured, or the network is down — enrichment is optional.
    /// </summary>
    public static void TryFetchInBackground(string ticketKey)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var provider = TicketProviders.For(ticketKey);
                if (provider is null) return;

                using (var conn = Db.Open())
                {
                    if (!TicketRepo.UnsyncedKeys(conn).Contains(ticketKey)) return;
                }

                await FetchOneAsync(provider, ticketKey);
            }
            catch
            {
                // lazy enrichment must never surface errors
            }
        });
    }

    /// <summary>Fetch a single key and record the result. Returns true when the key exists.</summary>
    public static async Task<bool> FetchOneAsync(ITicketProvider provider, string ticketKey)
    {
        var info = await provider.FetchAsync(ticketKey);
        using var conn = Db.Open();
        if (info is null)
        {
            TicketRepo.MarkFailed(conn, ticketKey);
            return false;
        }
        TicketRepo.Upsert(conn, info);
        return true;
    }
}
