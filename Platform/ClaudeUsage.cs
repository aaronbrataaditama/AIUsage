using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIUsage.Platform;

/// <summary>One bar on the Live Code usage panel. Pct is server-computed (0–100).</summary>
public sealed record UsageBar(string Id, string Label, double Pct, DateTime? ResetsAt, string? Detail);

/// <summary>Rolling usage snapshot from Anthropic's OAuth usage endpoint — the same one Claude
/// Code's <c>/usage</c> command reads. Every subscription surfaces a different subset of windows
/// (5-hour + 7-day for Pro; those plus per-model 7-day caps for Max/Team; a monthly spend limit
/// instead of any rolling window for Enterprise seats with a spend cap), so the bars are a flat,
/// ordered list rather than fixed fields. Percent + limits are computed server-side (no local quota
/// table); we just surface them.</summary>
public sealed record ClaudeUsageInfo(IReadOnlyList<UsageBar> Bars)
{
    public bool HasAny => Bars.Count > 0;
}

/// <summary>
/// Reads the rolling usage bars shown on the Live Code page. Authenticates the
/// <c>oauth/usage</c> GET with the access token from <c>~/.claude/.credentials.json</c>
/// (<c>claudeAiOauth.accessToken</c>) — the token is used only to sign this request and is never
/// stored, logged, or returned. Best-effort throughout: any problem (no token, expired, offline,
/// non-2xx) returns the last good value (or null) so the panel silently degrades rather than erroring.
/// The endpoint is hit at most once per <see cref="CacheTtl"/>; callers may poll freely.
/// </summary>
public static class ClaudeUsage
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static ClaudeUsageInfo? _cached;
    private static DateTime _attemptedAtUtc = DateTime.MinValue;

    public static async Task<ClaudeUsageInfo?> ReadAsync()
    {
        // Serialize + rate-limit: concurrent pollers share one in-flight fetch and the 5-min cache.
        await FetchLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _attemptedAtUtc < CacheTtl) return _cached;
            _attemptedAtUtc = DateTime.UtcNow;

            var token = ReadToken();
            if (token is null) return _cached; // not signed in / expired — keep last good

            using var req = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return _cached;

            _cached = Parse(await resp.Content.ReadAsStringAsync());
            return _cached;
        }
        catch
        {
            return _cached;
        }
        finally
        {
            FetchLock.Release();
        }
    }

    // Rolling windows, in display order. Unknown/codenamed keys (e.g. "nimbus_quill") are ignored —
    // we don't know what they mean, so we don't guess a label.
    private static readonly (string Id, string Label)[] Windows =
    [
        ("five_hour", "SESSION"),
        ("seven_day", "WEEK"),
        ("seven_day_opus", "WEEK · OPUS"),
        ("seven_day_sonnet", "WEEK · SONNET"),
    ];

    /// <summary>Map the endpoint's windows (rolling 5h/7d/per-model, or an Enterprise monthly spend
    /// cap in place of any rolling window) to display bars.</summary>
    internal static ClaudeUsageInfo Parse(string json)
    {
        var bars = new List<UsageBar>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var (id, label) in Windows)
                    ReadWindow(root, id, label, bars);

                if (root.TryGetProperty("extra_usage", out var x) && x.ValueKind == JsonValueKind.Object
                    && x.TryGetProperty("is_enabled", out var en) && en.ValueKind == JsonValueKind.True
                    && x.TryGetProperty("monthly_limit", out var lim) && lim.ValueKind == JsonValueKind.Number
                    && lim.TryGetDouble(out var limit) && limit > 0)
                {
                    // Every TryGet* below is guarded by a ValueKind == Number check first: the
                    // endpoint can send `null` for any of these (e.g. mid-rollout), and
                    // JsonElement.TryGetDouble/TryGetInt32 throw InvalidOperationException — not
                    // return false — for a non-Number element, which used to escape this try and
                    // blank the whole cached snapshot (SESSION/WEEK bars included).
                    var dp = x.TryGetProperty("decimal_places", out var d) && d.ValueKind == JsonValueKind.Number
                        && d.TryGetInt32(out var n) ? Math.Clamp(n, 0, 4) : 2;
                    var scale = Math.Pow(10, dp);
                    var used = x.TryGetProperty("used_credits", out var uc) && uc.ValueKind == JsonValueKind.Number
                        && uc.TryGetDouble(out var u) ? u : 0;
                    var reached = x.TryGetProperty("spend_limit_reached", out var r) && r.ValueKind == JsonValueKind.True;
                    var pct = reached ? 100
                        : x.TryGetProperty("utilization", out var ut) && ut.ValueKind == JsonValueKind.Number && ut.TryGetDouble(out var p)
                            ? p : used / limit * 100;
                    var cur = x.TryGetProperty("currency", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "USD";
                    string Money(double minor) => (cur == "USD" ? "$" : cur + " ") +
                        (minor / scale).ToString("N" + dp, System.Globalization.CultureInfo.InvariantCulture);
                    var detail = $"{Money(used)} / {Money(limit)}" + (reached ? " · limit reached" : "");
                    // "MONTHLY LIMIT" when this is the only window (Enterprise spend-capped seat, no
                    // 5h/7d rolling windows at all); "EXTRA USAGE" when it supplements Pro/Max/Team windows.
                    bars.Add(new UsageBar("extra_usage", bars.Count == 0 ? "MONTHLY LIMIT" : "EXTRA USAGE", pct, null, detail));
                }
            }
        }
        // Broadened from JsonException: a value of the wrong CLR type deep in extra_usage (e.g. a
        // string where a number was expected) throws InvalidOperationException, not JsonException —
        // either way, surface whatever windows parsed rather than losing the whole snapshot.
        catch (Exception) { /* malformed — surface whatever parsed */ }
        return new ClaudeUsageInfo(bars);
    }

    private static void ReadWindow(JsonElement root, string id, string label, List<UsageBar> bars)
    {
        if (!root.TryGetProperty(id, out var win) || win.ValueKind != JsonValueKind.Object) return;
        if (!win.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return;

        DateTime? resets = null;
        if (win.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
            && DateTime.TryParse(r.GetString(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var d))
            resets = d;

        bars.Add(new UsageBar(id, label, u.GetDouble(), resets, null));
    }

    /// <summary>OAuth access token from <c>~/.claude/.credentials.json</c>, or null if the file is
    /// missing, malformed, or the token has expired. Read only to authenticate the usage request.</summary>
    private static string? ReadToken()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o) || o.ValueKind != JsonValueKind.Object)
                return null;

            if (o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number
                && e.TryGetInt64(out var ms)
                && DateTimeOffset.FromUnixTimeMilliseconds(ms) <= DateTimeOffset.UtcNow)
                return null; // expired

            return o.TryGetProperty("accessToken", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
