using AIUsage.Platform;

namespace AIUsage.Tests;

public class ClaudeAccountTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);

    private static string Acct(string? org, string? tier, string? end = null) =>
        "{" + string.Join(",", new[] {
            org is null ? null : $"\"organizationType\":\"{org}\"",
            tier is null ? null : $"\"userRateLimitTier\":\"{tier}\"",
            end is null ? null : $"\"planLimitsEndDate\":\"{end}\"" }.Where(x => x is not null)) + "}";

    [Theory]
    [InlineData("claude_enterprise", "default_claude_zero", "Enterprise")]
    [InlineData("claude_enterprise", null, "Enterprise")]
    [InlineData("claude_team", "default_claude_max_5x", "Team · Max 5x")]
    [InlineData("claude_individual", "default_claude_max_20x", "Max 20x")]
    [InlineData(null, "default_claude_max_5x", "Max 5x")]
    [InlineData(null, "default_claude_pro", "Pro")]
    [InlineData(null, "default_claude_free", "Free")]
    public void Parse_labels_every_plan(string? org, string? tier, string expected) =>
        Assert.Equal(expected, ClaudeAccount.Parse(Acct(org, tier), Now).Plan);

    [Fact]
    public void Parse_hides_a_reset_date_in_the_past() =>
        Assert.Null(ClaudeAccount.Parse(Acct("claude_enterprise", "default_claude_zero", "2026-07-20T07:00:00Z"), Now).UsageResetsAt);

    [Fact]
    public void Parse_keeps_a_future_reset_date() =>
        Assert.NotNull(ClaudeAccount.Parse(Acct(null, "default_claude_pro", "2026-10-20T07:00:00Z"), Now).UsageResetsAt);

    [Fact]
    public void Parse_malformed_json_returns_nulls() =>
        Assert.Null(ClaudeAccount.Parse("{not json", Now).Plan);
}
