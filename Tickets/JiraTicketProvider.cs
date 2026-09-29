using AIUsage.Jira;

namespace AIUsage.Tickets;

/// <summary>Wraps <see cref="JiraClient"/> behind <see cref="ITicketProvider"/>.</summary>
public sealed class JiraTicketProvider(JiraClient client) : ITicketProvider
{
    /// <summary>Finished statuses (case-insensitive) — a JIRA issue in one of these is "done" for the
    /// Live Code "tickets to work on" picker and for <see cref="TicketInfo.IsDone"/>. Moved from
    /// LiveCodeHandlers.ExcludedTicketStatuses so both the picker and the DB agree on what "done" means.</summary>
    internal static readonly HashSet<string> DoneStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Closed", "Done", "Ready for Release" };

    /// <summary>JQL for "tickets assigned to me, newest first" — used by both the Live Code picker
    /// and <see cref="AssignedAsync"/>. Moved from LiveCodeHandlers.AssignedJql.</summary>
    internal const string AssignedJql = "assignee = currentUser() ORDER BY updated DESC";

    public string Id => TicketProviderIds.Jira;
    public string DisplayName => "JIRA";
    public string ItemNoun => "ticket";

    /// <summary>The underlying client, for the JQL-specific "Fetch more from JIRA" handler
    /// (`tickets.fetchMore`), which isn't part of the provider-neutral surface.</summary>
    public JiraClient Client => client;

    public async Task<TicketInfo?> FetchAsync(string key)
    {
        var issue = await client.FetchIssueAsync(key);
        return issue is null ? null : ToTicketInfo(issue);
    }

    public async Task<List<TicketInfo>> AssignedAsync(int max)
    {
        var page = await client.SearchIssuesAsync(AssignedJql, nextPageToken: null, maxResults: max);
        return page.Issues.Select(ToTicketInfo).ToList();
    }

    private static TicketInfo ToTicketInfo(JiraIssue issue) => new(
        Key: issue.Key,
        Provider: TicketProviderIds.Jira,
        Summary: issue.Summary,
        Status: issue.Status,
        IssueType: issue.IssueType,
        Project: issue.Project,
        Sprint: issue.Sprint,
        Priority: issue.Priority,
        Updated: issue.Updated,
        Description: issue.Description,
        IsDone: issue.Status is not null && DoneStatuses.Contains(issue.Status.Trim()));
}
