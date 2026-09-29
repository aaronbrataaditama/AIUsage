using AIUsage.ClickUp;
using AIUsage.Data;
using AIUsage.Data.Repositories;
using AIUsage.Jira;
using AIUsage.Settings;
using AIUsage.Tickets;

namespace AIUsage.Bridge.Handlers;

public static class TicketHandlers
{
    /// <summary>Default JQL for "Fetch more from JIRA" when the user hasn't set one.</summary>
    public const string DefaultFetchJql = "assignee = currentUser() ORDER BY updated DESC";

    /// <summary>Per-key result of a <c>tickets.sync</c> pass — kept separate from the counting so the
    /// tally can be tested without a network call.</summary>
    internal enum SyncOutcome { Ok, Dead, Failed, Skipped }

    /// <summary>Pure counting for <c>tickets.sync</c>: how many keys landed in each bucket.</summary>
    internal static (int ok, int dead, int failed, int skipped) Tally(IEnumerable<SyncOutcome> outcomes)
    {
        int ok = 0, dead = 0, failed = 0, skipped = 0;
        foreach (var outcome in outcomes)
        {
            switch (outcome)
            {
                case SyncOutcome.Ok: ok++; break;
                case SyncOutcome.Dead: dead++; break;
                case SyncOutcome.Failed: failed++; break;
                case SyncOutcome.Skipped: skipped++; break;
            }
        }
        return (ok, dead, failed, skipped);
    }

    private static string TrackerNameFor(string providerId) =>
        providerId == TicketProviderIds.ClickUp ? "ClickUp" : "JIRA";

    public static void Register(MessageRouter router)
    {
        router.Register("tickets.list", _ =>
        {
            using var conn = Db.Open();
            var rows = TicketRepo.List(conn);
            // Rows synced before the provider column existed (or never synced at all) have no
            // provider recorded — resolve it from config so the UI never sees a blank column.
            foreach (var row in rows)
                if (row.GetValueOrDefault("provider") is not string)
                    row["provider"] = TicketProviders.ProviderIdFor(row.GetValueOrDefault("key") as string ?? "");
            return Task.FromResult<object?>(rows);
        });

        router.Register("tickets.fetch", async payload =>
        {
            var key = TicketKey.Require(SessionHandlers.GetString(payload, "ticketKey"));
            var provider = TicketProviders.For(key) ?? throw new InvalidOperationException(
                $"{TrackerNameFor(TicketProviders.ProviderIdFor(key))} is not enabled/configured — see Settings");
            var found = await TicketSync.FetchOneAsync(provider, key);
            return new { found };
        });

        router.Register("tickets.sync", async _ =>
        {
            List<string> keys;
            using (var conn = Db.Open())
                keys = TicketRepo.AllKeys(conn);

            var outcomes = new List<SyncOutcome>();
            foreach (var key in keys)
            {
                var provider = TicketProviders.For(key);
                if (provider is null)
                {
                    // Tracker disabled/unconfigured — no network call, so no reason to throttle it.
                    outcomes.Add(SyncOutcome.Skipped);
                    continue;
                }
                try
                {
                    outcomes.Add(await TicketSync.FetchOneAsync(provider, key) ? SyncOutcome.Ok : SyncOutcome.Dead);
                }
                catch
                {
                    outcomes.Add(SyncOutcome.Failed);
                }
                await Task.Delay(150); // gentle on rate limits; also keeps ClickUp under its 100 req/min cap
            }

            var (ok, dead, failed, skipped) = Tally(outcomes);
            return new { synced = ok, dead, failed, skipped, total = keys.Count };
        });

        // Import additional tickets by page, one page per call. JIRA pages via its opaque
        // nextPageToken; ClickUp pages via a plain page number. Both are surfaced to the client
        // uniformly as "cursor" so the "Fetch more" button doesn't need to know which tracker it's
        // paging through.
        router.Register("tickets.fetchMore", async payload =>
        {
            var providerId = SessionHandlers.GetString(payload, "provider") ?? TicketProviderIds.Jira;
            var cursor = SessionHandlers.GetString(payload, "cursor");

            if (providerId == TicketProviderIds.ClickUp)
            {
                if (!SettingsStore.ClickUpEnabled() || ClickUpClient.FromSettings() is not { } client)
                    throw new InvalidOperationException("ClickUp is not enabled/configured — see Settings");

                var page = int.TryParse(cursor, out var p) ? p : 0;
                var (tasks, isLast) = await client.AssignedAsync(page);

                using (var conn = Db.Open())
                    foreach (var t in tasks)
                        TicketRepo.Upsert(conn, t);

                return new { imported = tasks.Count, cursor = isLast ? null : (page + 1).ToString(), isLast };
            }
            else
            {
                // Enabled-check goes through the provider system (so jira_enabled is honoured here
                // too), then we drop to the concrete client for SearchIssuesAsync/JQL paging, which
                // isn't part of the provider-neutral ITicketProvider surface.
                var jira = TicketProviders.Enabled().OfType<JiraTicketProvider>().FirstOrDefault()
                    ?? throw new InvalidOperationException("JIRA is not enabled/configured — see Settings");

                var jql = SettingsStore.Get("jira_fetch_jql");
                if (string.IsNullOrWhiteSpace(jql)) jql = DefaultFetchJql;

                var searchPage = await jira.Client.SearchIssuesAsync(jql, cursor);

                using (var conn = Db.Open())
                    foreach (var iss in searchPage.Issues)
                        TicketRepo.Upsert(conn, new TicketInfo(iss.Key, TicketProviderIds.Jira, iss.Summary, iss.Status,
                            iss.IssueType, iss.Project, iss.Sprint, iss.Priority, iss.Updated));

                return new { imported = searchPage.Issues.Count, cursor = searchPage.NextPageToken, isLast = searchPage.IsLast };
            }
        });

        router.Register("jira.test", async _ =>
        {
            // FromSettings also returns null for a non-https site URL (the Basic credential would go
            // out in cleartext), so say which of the two it is rather than "fill it in".
            var client = JiraClient.FromSettings() ?? throw new InvalidOperationException(
                JiraSiteUrl.IsSecure(SettingsStore.Get("jira_site_url")) || string.IsNullOrWhiteSpace(SettingsStore.Get("jira_site_url"))
                    ? "Fill in site URL, email and token first (and Save)"
                    : "The site URL must be an https:// address — the API token is sent as a reversible credential on every request. Fix it and Save.");
            var user = await client.TestConnectionAsync();
            return new { user };
        });

        router.Register("clickup.test", async _ =>
        {
            // Deliberately ignores clickup_enabled — this is the connectivity check the user runs
            // to set up ClickUp in the first place, before they've turned it on.
            var client = ClickUpClient.FromSettings()
                ?? throw new InvalidOperationException("Paste a ClickUp API token first (and Save)");
            var (user, workspaces) = await client.TestConnectionAsync();

            var teamId = SettingsStore.Get("clickup_team_id");
            if (workspaces.Count == 1 && string.IsNullOrWhiteSpace(teamId))
            {
                teamId = workspaces[0].Id;
                SettingsStore.Set("clickup_team_id", teamId);
            }

            return new
            {
                user,
                workspaces = workspaces.Select(w => new { id = w.Id, name = w.Name }),
                teamId = teamId ?? ""
            };
        });
    }
}
