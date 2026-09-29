using AIUsage.ClickUp;
using AIUsage.Data;
using AIUsage.Jira;
using AIUsage.Settings;

namespace AIUsage.Tickets;

/// <summary>Routes a ticket key to the tracker that owns it, and constructs the enabled providers.</summary>
public static class TicketProviders
{
    /// <summary>Which tracker owns a key, from config only (no network). Always returns an id.</summary>
    internal static string ProviderIdFor(string key, HashSet<string> clickUpPrefixes)
    {
        if (TicketKey.IsClickUpNative(key)) return TicketProviderIds.ClickUp;
        var project = TicketKey.ProjectOf(key);
        return project is not null && clickUpPrefixes.Contains(project)
            ? TicketProviderIds.ClickUp : TicketProviderIds.Jira;
    }

    /// <summary>Which tracker owns a key, from config only (no network). Always returns an id.</summary>
    public static string ProviderIdFor(string key) => ProviderIdFor(key, SettingsStore.ClickUpCustomIdPrefixes());

    /// <summary>The enabled + configured provider for this key, or null.</summary>
    public static ITicketProvider? For(string key) => ProviderIdFor(key) switch
    {
        TicketProviderIds.ClickUp => SettingsStore.ClickUpEnabled() && ClickUpClient.FromSettings() is { } c
            ? new ClickUpTicketProvider(c) : null,
        _ => SettingsStore.JiraEnabled() && JiraClient.FromSettings() is { } j
            ? new JiraTicketProvider(j) : null,
    };

    /// <summary>All enabled + configured providers (for the merged picker).</summary>
    public static List<ITicketProvider> Enabled()
    {
        var list = new List<ITicketProvider>();
        if (SettingsStore.JiraEnabled() && JiraClient.FromSettings() is { } j) list.Add(new JiraTicketProvider(j));
        if (SettingsStore.ClickUpEnabled() && ClickUpClient.FromSettings() is { } c) list.Add(new ClickUpTicketProvider(c));
        return list;
    }
}
