using AIUsage.ClickUp;
using AIUsage.Tickets;

namespace AIUsage.Tests;

public class TicketProvidersTests
{
    private static readonly HashSet<string> Prefixes = ["DEV", "OPS"];

    [Theory]
    [InlineData("86b1abcde", "clickup")]
    [InlineData("DEV-42", "clickup")]
    [InlineData("SFTY-1", "jira")]
    public void ProviderIdFor_routes_by_form_and_prefix(string key, string expected) =>
        Assert.Equal(expected, TicketProviders.ProviderIdFor(key, Prefixes));

    [Fact]
    public void For_routes_digit_only_CU_to_jira() =>
        Assert.Equal("jira", TicketProviders.ProviderIdFor("CU-123456", Prefixes));

    [Fact]
    public async Task ClickUpProvider_assigned_without_team_throws_readable()
    {
        var provider = new ClickUpTicketProvider(ClickUpClient.ForTest("pk_x", null));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.AssignedAsync(5));
        Assert.Contains("workspace", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
