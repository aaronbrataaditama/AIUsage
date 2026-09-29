using System.Text.Json;
using System.Text.RegularExpressions;
using AIUsage.Data;
using AIUsage.Jira;
using AIUsage.Settings;
using Microsoft.Data.Sqlite;

namespace AIUsage.Bridge.Handlers;

public static partial class SettingsHandlers
{
    // ClickUp Custom Task ID prefixes, e.g. "DEV" — same shape as a JIRA project key, so reuse
    // the pattern rather than re-derive it.
    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}$")]
    private static partial Regex PrefixPattern();

    [GeneratedRegex(@"^\d{1,20}$")]
    private static partial Regex TeamIdPattern();

    public static void Register(MessageRouter router)
    {
        // Synchronous handlers return Task.FromResult (no Task.Run) — see the note in
        // SessionHandlers.Register on the null-return / unwrap-overload cancellation trap.
        router.Register("settings.get", _ => Task.FromResult<object?>(new
        {
            jiraSiteUrl = SettingsStore.Get("jira_site_url") ?? "",
            // A stored non-https URL disables JIRA (JiraClient.FromSettings returns null) rather
            // than sending the Basic credential in cleartext — the page explains that instead of
            // showing a bare "not configured".
            jiraSiteUrlInsecure = !string.IsNullOrWhiteSpace(SettingsStore.Get("jira_site_url"))
                                  && !JiraSiteUrl.IsSecure(SettingsStore.Get("jira_site_url")),
            jiraEmail = SettingsStore.Get("jira_email") ?? "",
            jiraTokenSet = SettingsStore.GetProtected("jira_token") is not null,
            jiraEnabled = SettingsStore.JiraEnabled(),
            scanPaths = SettingsStore.Get("scan_paths") ?? "",
            defaultScanPath = SettingsStore.ScanRoots()[0],
            projectKeyAllowlist = SettingsStore.Get("project_key_allowlist") ?? "",
            backfillFrom = SettingsStore.Get("backfill_from") ?? "",
            jiraFetchJql = SettingsStore.Get("jira_fetch_jql") ?? TicketHandlers.DefaultFetchJql,
            livecodeTicketCount = SettingsStore.Get("livecode_ticket_count") ?? "3",
            clickupEnabled = SettingsStore.ClickUpEnabled(),
            clickupTokenSet = SettingsStore.GetProtected("clickup_token") is not null,
            clickupTeamId = SettingsStore.Get("clickup_team_id") ?? "",
            clickupCustomIdPrefixes = SettingsStore.Get("clickup_custom_id_prefixes") ?? ""
        }));

        router.Register("settings.set", payload =>
        {
            // The site URL is validated, not just trimmed: it's where a reversible Basic credential
            // gets sent. An invalid value throws before anything is written (the UI toasts the
            // message); an empty value clears the setting. If the host changes, the stored token is
            // dropped — it belongs to the old host and must not be replayable to a new one.
            var tokenCleared = false;
            var purgeNeeded = false;
            var siteRaw = SessionHandlers.GetString(payload, "jiraSiteUrl");
            if (siteRaw is not null)
            {
                if (string.IsNullOrWhiteSpace(siteRaw))
                {
                    SettingsStore.Set("jira_site_url", "");
                }
                else
                {
                    var site = JiraSiteUrl.Normalize(siteRaw);
                    if (JiraSiteUrl.PointsAtADifferentHost(SettingsStore.Get("jira_site_url"), site)
                        && SettingsStore.GetProtected("jira_token") is not null)
                    {
                        SettingsStore.Set("jira_token", null);
                        tokenCleared = true;
                    }
                    SettingsStore.Set("jira_site_url", site);
                }
            }
            SetIfPresent(payload, "jiraEmail", "jira_email");
            SetIfPresent(payload, "scanPaths", "scan_paths");
            SetIfPresent(payload, "backfillFrom", "backfill_from");
            SetIfPresent(payload, "jiraFetchJql", "jira_fetch_jql");

            // Live Code ticket count: store a clamped integer (1..20) so the picker/config stay sane.
            var count = SessionHandlers.GetString(payload, "livecodeTicketCount");
            if (count is not null && int.TryParse(count.Trim(), out var n))
                SettingsStore.Set("livecode_ticket_count", Math.Clamp(n, 1, 20).ToString());

            // token is write-only: only overwrite when a new non-empty value arrives
            var token = SessionHandlers.GetString(payload, "jiraToken");
            if (!string.IsNullOrWhiteSpace(token))
                SettingsStore.SetProtected("jira_token", token.Trim());

            var newAllowlist = SessionHandlers.GetString(payload, "projectKeyAllowlist");
            if (newAllowlist is not null)
            {
                var before = SettingsStore.Get("project_key_allowlist") ?? "";
                SettingsStore.Set("project_key_allowlist", newAllowlist.Trim());
                if (!string.Equals(before.Trim(), newAllowlist.Trim(), StringComparison.OrdinalIgnoreCase))
                    purgeNeeded = true;
            }

            if (TryGetBool(payload, "jiraEnabled", out var jiraEnabled))
                SettingsStore.Set("jira_enabled", jiraEnabled ? "1" : "0");

            if (TryGetBool(payload, "clickupEnabled", out var clickupEnabled))
            {
                if (clickupEnabled != SettingsStore.ClickUpEnabled()) purgeNeeded = true;
                SettingsStore.Set("clickup_enabled", clickupEnabled ? "1" : "0");
            }

            // ClickUp token is write-only, same as the JIRA one: only overwrite on a new non-empty value.
            var clickupToken = SessionHandlers.GetString(payload, "clickupToken");
            if (!string.IsNullOrWhiteSpace(clickupToken))
                SettingsStore.SetProtected("clickup_token", clickupToken.Trim());

            var clickupTeamId = SessionHandlers.GetString(payload, "clickupTeamId");
            if (clickupTeamId is not null)
            {
                var trimmed = clickupTeamId.Trim();
                if (trimmed.Length > 0 && !TeamIdPattern().IsMatch(trimmed))
                    throw new ArgumentException($"'{trimmed}' is not a valid ClickUp workspace id (expected digits only)");
                SettingsStore.Set("clickup_team_id", trimmed);
            }

            var prefixesRaw = SessionHandlers.GetString(payload, "clickupCustomIdPrefixes");
            if (prefixesRaw is not null)
            {
                var before = SettingsStore.Get("clickup_custom_id_prefixes") ?? "";
                var parsed = ParsePrefixes(prefixesRaw);
                SettingsStore.Set("clickup_custom_id_prefixes", parsed);
                if (!string.Equals(before, parsed, StringComparison.Ordinal))
                    purgeNeeded = true;
            }

            if (purgeNeeded) PurgeDisallowedAutoLinks();
            return Task.FromResult<object?>(new { tokenCleared });
        });
    }

    private static void SetIfPresent(System.Text.Json.JsonElement payload, string jsonName, string settingKey)
    {
        var value = SessionHandlers.GetString(payload, jsonName);
        if (value is not null)
            SettingsStore.Set(settingKey, value.Trim());
    }

    private static bool TryGetBool(JsonElement payload, string name, out bool value)
    {
        value = false;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var p))
            return false;
        switch (p.ValueKind)
        {
            case JsonValueKind.True: value = true; return true;
            case JsonValueKind.False: value = false; return true;
            default: return false;
        }
    }

    /// <summary>Normalizes a comma-separated list of ClickUp Custom Task ID prefixes (e.g.
    /// "dev, ops" -&gt; "DEV,OPS"), throwing a readable message for anything that isn't a bare
    /// project-key-shaped token.</summary>
    internal static string ParsePrefixes(string raw)
    {
        var prefixes = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToUpperInvariant())
            .ToList();
        foreach (var p in prefixes)
            if (!PrefixPattern().IsMatch(p))
                throw new ArgumentException($"'{p}' is not a valid ClickUp Custom ID prefix (expected e.g. DEV)");
        return string.Join(",", prefixes);
    }

    /// <summary>
    /// After the allowlist changes, drop auto-inferred links whose project key no longer
    /// matches (manual and confirmed links are user statements — never touched), demote
    /// sessions left without links back to the review queue, and remove orphaned,
    /// never-synced ticket rows.
    /// </summary>
    public static void PurgeDisallowedAutoLinks()
    {
        var allowed = SettingsStore.EffectiveKeyAllowlist();
        if (allowed.Count == 0) return; // empty allowlist = allow everything

        using var conn = Db.Open();
        PurgeDisallowedAutoLinks(conn, allowed);
    }

    /// <summary>
    /// The purge itself, against an explicit connection. Native ClickUp links (CU-&lt;id&gt;) have no
    /// project part, so the allowlist never applies to them and they are always kept.
    /// </summary>
    internal static void PurgeDisallowedAutoLinks(SqliteConnection conn, HashSet<string> allowed)
    {
        if (allowed.Count == 0) return; // empty allowlist = allow everything

        using var tx = conn.BeginTransaction();

        var placeholders = string.Join(",", allowed.Select((_, i) => $"$p{i}"));
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                DELETE FROM SessionTicketLinks
                WHERE source = 'auto'
                  AND ticket_key NOT GLOB '{TicketKey.ClickUpNativeGlob}'
                  AND substr(ticket_key, 1, instr(ticket_key, '-') - 1) NOT IN ({placeholders});

                UPDATE Sessions SET review_state = 'pending'
                WHERE review_state = 'linked'
                  AND NOT EXISTS (SELECT 1 FROM SessionTicketLinks l WHERE l.session_id = Sessions.id);

                DELETE FROM Tickets
                WHERE last_synced IS NULL AND fetch_failed = 0
                  AND NOT EXISTS (SELECT 1 FROM SessionTicketLinks l WHERE l.ticket_key = Tickets.key)
                  AND NOT EXISTS (SELECT 1 FROM ManualEntries m WHERE m.ticket_key = Tickets.key);
                """;
            for (var i = 0; i < allowed.Count; i++)
                cmd.Parameters.AddWithValue($"$p{i}", allowed.ElementAt(i));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
