using System.Text.RegularExpressions;

namespace AIUsage.Data;

/// <summary>
/// The single definition of a valid JIRA ticket key (uppercase project key + number), shared by
/// every writer of <c>SessionTicketLinks.ticket_key</c>: the bridge handlers, the manual-entry
/// handler, the Live Code launcher and the repositories themselves. Centralised deliberately —
/// the Live Code path used to be the only unconstrained writer of that column, and its value
/// comes from a remote JIRA server (2026-08 audit, AIU-04).
/// Also accepts ClickUp native task ids in their Git-integration form, "CU-" + lowercase id.
/// </summary>
public static partial class TicketKey
{
    // \z, not $: .NET's $ also matches before a trailing "\n", and a key ending in a newline would
    // become an Enter keystroke when typed into the Live Code shell.
    [GeneratedRegex(@"^[A-Z][A-Z0-9]{1,9}-\d{1,6}\z")]
    private static partial Regex Pattern();

    // ClickUp native task id in the form ClickUp's own Git integration writes it: "CU-" + the
    // lowercase id (e.g. CU-86b1abcde). It must contain a letter so a JIRA project literally named
    // CU ("CU-123456", digits only) stays a JIRA key. The id is case-sensitive in the ClickUp API.
    [GeneratedRegex(@"^CU-(?=[0-9a-z]*[a-z])[0-9a-z]{6,12}\z")]
    private static partial Regex ClickUpNativePattern();

    /// <summary>SQL GLOB (case-sensitive) matching exactly the stored native-ClickUp form's prefix+letter.</summary>
    public const string ClickUpNativeGlob = "CU-*[a-z]*";

    /// <summary>True for an already-normalised key: "SFTY-1234" or "CU-86b1abcde".</summary>
    public static bool IsValid(string? key) =>
        key is not null && (Pattern().IsMatch(key) || ClickUpNativePattern().IsMatch(key));

    public static bool IsClickUpNative(string? key) => key is not null && ClickUpNativePattern().IsMatch(key);

    /// <summary>Trim; ClickUp native ids keep a lowercase body, everything else is uppercased.</summary>
    public static string Normalize(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length > 3 && t.StartsWith("CU-", StringComparison.OrdinalIgnoreCase))
        {
            var native = "CU-" + t[3..].ToLowerInvariant();
            if (ClickUpNativePattern().IsMatch(native)) return native;
        }
        return t.ToUpperInvariant();
    }

    /// <summary>Allowlist project part ("SFTY" for SFTY-1); null for a ClickUp native key or a
    /// dash-less key (e.g. a stray NULL-provider row) rather than throwing.</summary>
    public static string? ProjectOf(string key)
    {
        if (IsClickUpNative(key)) return null;
        var i = key.IndexOf('-');
        return i > 0 ? key[..i] : null;
    }

    /// <summary>Normalise and validate, throwing the message the UI toasts on failure.</summary>
    public static string Require(string? raw)
    {
        var key = Normalize(raw);
        if (!IsValid(key))
            throw new ArgumentException($"'{key}' is not a valid ticket key (expected e.g. SFTY-1234 or CU-86b1abcde)");
        return key;
    }
}
