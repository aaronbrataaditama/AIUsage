using AIUsage.Data;

namespace AIUsage.Tests;

/// <summary>
/// The shared ticket-key validator. Every writer of <c>SessionTicketLinks.ticket_key</c> goes
/// through this so no path (the Live Code one used to) can persist an unconstrained key that
/// later renders in the UI.
/// </summary>
public class TicketKeyTests
{
    [Theory]
    [InlineData("ABC-1")]
    [InlineData("SFTY-1234")]
    [InlineData("AB1-999999")]
    [InlineData("ABCDEFGHIJ-1")]
    public void IsValid_accepts_real_keys(string key) => Assert.True(TicketKey.IsValid(key));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("A-1")]                 // project key too short
    [InlineData("ABCDEFGHIJK-1")]       // project key too long
    [InlineData("ABC-1234567")]         // number too long
    [InlineData("ABC-")]
    [InlineData("abc-1")]               // must already be uppercased
    [InlineData("X'+alert(1)+'Y")]
    [InlineData("ABC-1; calc")]
    [InlineData("ABC-1\nABC-2")]
    [InlineData("ABC-1\n")]             // .NET '$' would match before a trailing newline
    public void IsValid_rejects_anything_else(string? key) => Assert.False(TicketKey.IsValid(key));

    [Fact]
    public void Require_trims_and_uppercases()
    {
        Assert.Equal("ABC-12", TicketKey.Require("  abc-12 "));
    }

    [Fact]
    public void Require_throws_on_an_invalid_key()
    {
        var ex = Assert.Throws<ArgumentException>(() => TicketKey.Require("X'+alert(1)+'Y"));
        Assert.Contains("not a valid ticket key", ex.Message);
    }

    [Theory]
    [InlineData("CU-86b1abcde")]
    [InlineData("CU-abc123")]
    [InlineData("CU-9hz4k2")]
    public void IsValid_accepts_clickup_native_keys(string key) => Assert.True(TicketKey.IsValid(key));

    [Theory]
    [InlineData("CU-86B1ABCDE")]        // not normalised — native ids are stored lowercase
    [InlineData("CU-abc")]              // too short (< 6)
    [InlineData("CU-abcdefghijklm")]    // too long (> 12)
    [InlineData("CU-86b1'abc")]
    [InlineData("CU-86b1 abc")]
    [InlineData("CU-86b1abcde\n")]
    public void IsValid_rejects_malformed_clickup_keys(string key) => Assert.False(TicketKey.IsValid(key));

    [Theory]
    [InlineData("  cu-86B1ABCDE ", "CU-86b1abcde")]
    [InlineData("CU-86b1abcde", "CU-86b1abcde")]
    [InlineData("sfty-12", "SFTY-12")]
    public void Normalize_folds_each_form_correctly(string raw, string expected) =>
        Assert.Equal(expected, TicketKey.Normalize(raw));

    [Theory]
    [InlineData("cu-123", "CU-123")]
    [InlineData("CU-123456", "CU-123456")]
    public void Normalize_keeps_digit_only_CU_keys_as_jira(string raw, string expected)
    {
        var key = TicketKey.Normalize(raw);
        Assert.Equal(expected, key);
        Assert.False(TicketKey.IsClickUpNative(key));
        Assert.True(TicketKey.IsValid(key));
    }

    [Fact]
    public void ProjectOf_is_null_for_native_and_prefix_for_jira_style()
    {
        Assert.Null(TicketKey.ProjectOf("CU-86b1abcde"));
        Assert.Equal("DEV", TicketKey.ProjectOf("DEV-123"));
    }

    [Fact]
    public void Require_message_mentions_both_forms()
    {
        var ex = Assert.Throws<ArgumentException>(() => TicketKey.Require("nope"));
        Assert.Contains("CU-", ex.Message);
    }
}
