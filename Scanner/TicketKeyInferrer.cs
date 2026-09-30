using System.Text.RegularExpressions;

namespace AIUsage.Scanner;

public sealed partial class TicketKeyInferrer(HashSet<string> projectKeyAllowlist, bool clickUpNative = false)
{
    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{1,9}-\d{1,6}\b")]
    private static partial Regex KeyRegex();

    // A bare ClickUp task id (no invented prefix — see TicketKey), bounded by non-alphanumeric
    // characters so it isn't picked out of the middle of an unrelated word or token.
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?=[0-9a-z]*[0-9])(?=[0-9a-z]*[a-z])[0-9a-z]{6,12}(?![0-9a-z])",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClickUpNativeRegex();

    private static readonly HashSet<string> NonBranches = ["", "HEAD", "main", "master"];

    public bool IsRealBranch(string? branch) =>
        branch is not null && !NonBranches.Contains(branch.Trim());

    /// <summary>Extract ticket keys from arbitrary text, filtered by the project-key allowlist.</summary>
    public IEnumerable<string> Extract(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (Match m in KeyRegex().Matches(text))
        {
            var key = m.Value;
            var project = key[..key.IndexOf('-')];
            if (projectKeyAllowlist.Count == 0 || projectKeyAllowlist.Contains(project))
                yield return key;
        }
        if (!clickUpNative) yield break;
        // Native ids are unambiguous ClickUp references, so the project allowlist doesn't apply.
        foreach (Match m in ClickUpNativeRegex().Matches(text))
        {
            var key = Data.TicketKey.Normalize(m.Value);
            if (Data.TicketKey.IsClickUpNative(key)) yield return key;
        }
    }
}
