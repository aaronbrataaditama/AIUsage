using System.Text.RegularExpressions;

namespace AIUsage.Data;

/// <summary>
/// The single definition of a valid JIRA ticket key (uppercase project key + number), shared by
/// every writer of <c>SessionTicketLinks.ticket_key</c>: the bridge handlers, the manual-entry
/// handler, the Live Code launcher and the repositories themselves. Centralised deliberately —
/// the Live Code path used to be the only unconstrained writer of that column, and its value
/// comes from a remote JIRA server (2026-08 audit, AIU-04).
/// Also accepts a bare ClickUp native task id (e.g. "86b1abcde") — exactly the id ClickUp itself
/// shows (its task URL, "Copy ID"), with no invented prefix.
/// </summary>
public static partial class TicketKey
{
    // \z, not $: .NET's $ also matches before a trailing "\n", and a key ending in a newline would
    // become an Enter keystroke when typed into the Live Code shell.
    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}-\d{1,6}\z")]
    private static partial Regex Pattern();

    // A bare ClickUp task id (e.g. 86b1abcde). JIRA-style and ClickUp Custom Task ID keys are
    // always "PROJECT-number", so the dash alone tells the two grammars apart — no prefix needed.
    // Requires both a letter AND a digit so a plain lowercase word (a branch segment like
    // "hotfix") never passes as a task id. The id is case-sensitive in the ClickUp API, so the
    // canonical stored form keeps the lowercase body.
    [GeneratedRegex(@"^(?=[0-9a-z]*[a-z])(?=[0-9a-z]*[0-9])[0-9a-z]{6,12}\z")]
    private static partial Regex ClickUpNativePattern();

    /// <summary>True for an already-normalised key: "SFTY-1234" or "86b1abcde".</summary>
    public static bool IsValid(string? key) =>
        key is not null && (Pattern().IsMatch(key) || ClickUpNativePattern().IsMatch(key));

    public static bool IsClickUpNative(string? key) => key is not null && ClickUpNativePattern().IsMatch(key);

    /// <summary>Trim; a dash-less ClickUp native id keeps a lowercase body, everything else is uppercased.</summary>
    public static string Normalize(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (!t.Contains('-'))
        {
            var lower = t.ToLowerInvariant();
            if (ClickUpNativePattern().IsMatch(lower)) return lower;
        }
        return t.ToUpperInvariant();
    }

    /// <summary>Allowlist project part ("SFTY" for SFTY-1); null for a ClickUp native key or a
    /// dash-less key (e.g. a stray NULL-provider row) rather than throwing.</summary>
    public static string? ProjectOf(string key)
    {
        var i = key.IndexOf('-');
        return i > 0 ? key[..i] : null;
    }

    /// <summary>Normalise and validate, throwing the message the UI toasts on failure.</summary>
    public static string Require(string? raw)
    {
        var key = Normalize(raw);
        if (!IsValid(key))
            throw new ArgumentException($"'{key}' is not a valid ticket key (expected e.g. SFTY-1234, or a bare ClickUp task id like 86d353g15)");
        return key;
    }
}
