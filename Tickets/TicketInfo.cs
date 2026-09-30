namespace AIUsage.Tickets;

public static class TicketProviderIds
{
    public const string Jira = "jira";
    public const string ClickUp = "clickup";
}

/// <summary>
/// A ticket as any tracker reports it, mapped onto the columns the Tickets table already has.
/// JIRA: IssueType = issue type, Project = project, Sprint = active sprint.
/// ClickUp: IssueType = custom task type (or "Task"), Project = folder/list, Sprint = list when it
/// lives in a sprint folder. IsDone = the tracker says the status is a finished one. PriorityRank
/// (0 = highest) orders the Live Code picker ahead of recency; JIRA tickets are all rank 0 (today's
/// pure-recency behavior, unaffected), while <see cref="AIUsage.Tickets.ClickUpTicketProvider"/>
/// assigns ClickUp's fixed status-name tiers (and excludes anything outside them).
/// </summary>
public sealed record TicketInfo(
    string Key, string Provider, string? Summary, string? Status, string? IssueType,
    string? Project, string? Sprint, string? Priority, string? Updated,
    string? Description = null, bool IsDone = false, int PriorityRank = 0);
