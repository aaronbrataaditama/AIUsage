using System.Net;
using System.Text.Json;
using AIUsage.Data;
using AIUsage.Settings;
using AIUsage.Tickets;

namespace AIUsage.ClickUp;

/// <summary>
/// Read-only ClickUp REST v2 client (personal API token). The host is a constant on purpose: unlike
/// JIRA there is no user-supplied base URL, so the token can never be sent to another host or over
/// http:// (the class of bug JiraSiteUrl exists for).
/// </summary>
public sealed class ClickUpClient
{
    private const string BaseUrl = "https://api.clickup.com/api/v2";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly string _token;
    private readonly string? _teamId;
    private long? _userId;

    private ClickUpClient(string token, string? teamId) { _token = token; _teamId = teamId; }

    public static ClickUpClient? FromSettings()
    {
        var token = SettingsStore.GetProtected("clickup_token");
        if (string.IsNullOrWhiteSpace(token)) return null;
        var team = SettingsStore.Get("clickup_team_id");
        return new ClickUpClient(token.Trim(), string.IsNullOrWhiteSpace(team) ? null : team.Trim());
    }

    /// <summary>Test-only construction (no Settings dependency) — used by ClickUpClientTests and by
    /// Task 4's provider-adapter tests to exercise the no-workspace error path.</summary>
    internal static ClickUpClient ForTest(string token, string? teamId) => new(token, teamId);

    public async Task<TicketInfo?> FetchTaskAsync(string key)
    {
        var (code, body) = await GetAsync(BuildTaskPath(key, _teamId));
        if (IsNotFound(code, body)) return null;
        EnsureOk(code, body);
        using var doc = JsonDocument.Parse(body);
        return ParseTask(doc.RootElement, key);
    }

    public async Task<(List<TicketInfo> Tasks, bool IsLast)> AssignedAsync(int page)
    {
        var team = _teamId ?? throw new InvalidOperationException(
            "ClickUp workspace not set — run Test connection in Settings and pick a workspace");
        _userId ??= await GetUserIdAsync();
        var (code, body) = await GetAsync(
            $"/team/{Uri.EscapeDataString(team)}/task?assignees[]={_userId}&order_by=updated" +
            $"&subtasks=true&include_closed=false&include_markdown_description=false&page={page}");
        EnsureOk(code, body);
        using var doc = JsonDocument.Parse(body);
        var list = ParseTaskList(doc.RootElement);
        var isLast = !doc.RootElement.TryGetProperty("last_page", out var lp) || lp.ValueKind != JsonValueKind.False;
        return (list, isLast);
    }

    public async Task<(string User, List<(string Id, string Name)> Workspaces)> TestConnectionAsync()
    {
        var (uc, ub) = await GetAsync("/user");
        EnsureOk(uc, ub);
        string user;
        using (var d = JsonDocument.Parse(ub))
            user = d.RootElement.TryGetProperty("user", out var u) ? Str(u, "username") ?? "(unknown user)" : "(unknown user)";

        var (tc, tb) = await GetAsync("/team");
        EnsureOk(tc, tb);
        var teams = new List<(string, string)>();
        using (var d = JsonDocument.Parse(tb))
            if (d.RootElement.TryGetProperty("teams", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var t in arr.EnumerateArray())
                    if (Str(t, "id") is { } id) teams.Add((id, Str(t, "name") ?? id));
        return (user, teams);
    }

    internal static string BuildTaskPath(string key, string? teamId)
    {
        if (TicketKey.IsClickUpNative(key))
            return $"/task/{Uri.EscapeDataString(key[3..])}?include_markdown_description=true";
        if (string.IsNullOrWhiteSpace(teamId))
            throw new InvalidOperationException(
                $"'{key}' is a ClickUp Custom Task ID — set the ClickUp workspace in Settings first");
        return $"/task/{Uri.EscapeDataString(key)}?custom_task_ids=true&team_id={Uri.EscapeDataString(teamId)}&include_markdown_description=true";
    }

    internal static TicketInfo ParseTask(JsonElement t, string? requestedKey)
    {
        var id = Str(t, "id") ?? "";
        var custom = TicketKey.Normalize(Str(t, "custom_id"));
        var key = requestedKey
                  ?? (TicketKey.IsValid(custom) && !TicketKey.IsClickUpNative(custom) ? custom : TicketKey.Normalize("CU-" + id));

        var statusType = t.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Object ? Str(s, "type") : null;
        var folder = Nested(t, "folder", "name");
        var list = Nested(t, "list", "name");
        var sprint = folder is not null && folder.Contains("sprint", StringComparison.OrdinalIgnoreCase) ? list : null;
        var desc = Str(t, "markdown_description") ?? Str(t, "text_content");

        return new TicketInfo(
            Key: key,
            Provider: TicketProviderIds.ClickUp,
            Summary: Str(t, "name"),
            Status: Nested(t, "status", "status"),
            IssueType: t.TryGetProperty("custom_item_id", out var ci) && ci.ValueKind == JsonValueKind.Number ? "Custom" : "Task",
            Project: folder ?? list,
            Sprint: sprint,
            Priority: Nested(t, "priority", "priority"),
            Updated: long.TryParse(Str(t, "date_updated"), out var ms)
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("o") : null,
            Description: string.IsNullOrWhiteSpace(desc) ? null : desc.Trim(),
            IsDone: statusType is "done" or "closed");
    }

    /// <summary>Parses the "tasks" array of an /team/{team}/task response, dropping any task whose
    /// id (with no usable custom_id) doesn't fold into a valid key — never persist/return a bad key.</summary>
    internal static List<TicketInfo> ParseTaskList(JsonElement root)
    {
        var list = new List<TicketInfo>();
        if (root.TryGetProperty("tasks", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var t in arr.EnumerateArray())
            {
                if (Str(t, "id") is null) continue;
                var info = ParseTask(t, null) with { Description = null };
                if (TicketKey.IsValid(info.Key)) list.Add(info);
            }
        return list;
    }

    internal static bool IsNotFound(HttpStatusCode code, string body)
    {
        if (code == HttpStatusCode.NotFound) return true;
        if (code is not (HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)) return false;
        try
        {
            using var d = JsonDocument.Parse(body);
            return Str(d.RootElement, "ECODE") is { } e && e.StartsWith("ITEM_", StringComparison.Ordinal);
        }
        catch (JsonException) { return false; }
    }

    private async Task<long> GetUserIdAsync()
    {
        var (c, b) = await GetAsync("/user");
        EnsureOk(c, b);
        using var d = JsonDocument.Parse(b);
        return d.RootElement.GetProperty("user").GetProperty("id").GetInt64();
    }

    private async Task<(HttpStatusCode, string)> GetAsync(string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
        req.Headers.TryAddWithoutValidation("Authorization", _token);
        req.Headers.Accept.ParseAdd("application/json");
        using var resp = await Http.SendAsync(req);
        return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static void EnsureOk(HttpStatusCode code, string body)
    {
        if ((int)code is >= 200 and < 300) return;
        var detail = code switch
        {
            HttpStatusCode.Unauthorized => "authentication failed — check the ClickUp API token",
            HttpStatusCode.Forbidden => "access denied — check workspace permissions",
            (HttpStatusCode)429 => "rate limited — try again in a minute",
            _ => body is { Length: > 0 and < 300 } ? body : "request failed"
        };
        throw new HttpRequestException($"ClickUp {(int)code}: {detail}");
    }

    private static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;

    private static string? Nested(JsonElement el, string obj, string name) =>
        el.TryGetProperty(obj, out var o) && o.ValueKind == JsonValueKind.Object ? Str(o, name) : null;
}
