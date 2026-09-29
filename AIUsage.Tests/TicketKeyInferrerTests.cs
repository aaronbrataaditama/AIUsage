using AIUsage.Scanner;

namespace AIUsage.Tests;

public class TicketKeyInferrerTests
{
    private static TicketKeyInferrer WithAllowlist(params string[] keys) => new([.. keys]);

    [Fact]
    public void Extract_keeps_keys_on_the_allowlist_and_drops_others()
    {
        var inferrer = WithAllowlist("ABC");
        var keys = inferrer.Extract("Fixing ABC-123 and XYZ-9 today").ToList();

        Assert.Contains("ABC-123", keys);
        Assert.DoesNotContain("XYZ-9", keys);
    }

    [Fact]
    public void Extract_with_empty_allowlist_returns_all_matches()
    {
        var inferrer = WithAllowlist(); // empty => allow all projects
        var keys = inferrer.Extract("ABC-1 and XYZ-9").ToList();

        Assert.Equal(new[] { "ABC-1", "XYZ-9" }, keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no ticket keys here")]
    [InlineData("lowercase abc-1 is not a key")]
    public void Extract_returns_nothing_when_there_is_no_valid_key(string? text)
    {
        var inferrer = WithAllowlist(); // allow-all so filtering isn't the reason
        Assert.Empty(inferrer.Extract(text));
    }

    [Fact]
    public void Extract_finds_multiple_keys_across_text()
    {
        var inferrer = WithAllowlist();
        var keys = inferrer.Extract("branch feature/ABC-12 closes ABC-34").ToList();

        Assert.Equal(2, keys.Count);
        Assert.Contains("ABC-12", keys);
        Assert.Contains("ABC-34", keys);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("HEAD", false)]
    [InlineData("main", false)]
    [InlineData("master", false)]
    [InlineData(" main ", false)] // trimmed before comparison
    [InlineData("feature/ABC-1", true)]
    [InlineData("develop", true)]
    public void IsRealBranch_rejects_placeholder_and_default_branches(string? branch, bool expected)
    {
        var inferrer = WithAllowlist();
        Assert.Equal(expected, inferrer.IsRealBranch(branch));
    }

    [Fact]
    public void Extract_ignores_native_clickup_when_disabled()
    {
        var inf = new TicketKeyInferrer([]);
        Assert.Empty(inf.Extract("CU-86b1abcde_fix-login"));
    }

    [Theory]
    [InlineData("CU-86b1abcde_fix-login_aaron", "CU-86b1abcde")]   // ClickUp Git-integration branch
    [InlineData("feature/cu-86b1abcde-fix", "CU-86b1abcde")]
    [InlineData("see CU-9hz4k2 please", "CU-9hz4k2")]
    public void Extract_finds_native_clickup_when_enabled(string text, string expected)
    {
        var inf = new TicketKeyInferrer([], clickUpNative: true);
        Assert.Equal([expected], inf.Extract(text).ToList());
    }

    [Theory]
    [InlineData("CU-abcdef")]    // no digit — too word-like to infer from free text
    [InlineData("accu-86b1abcde")] // embedded in a word
    public void Extract_does_not_infer_wordlike_native(string text)
    {
        var inf = new TicketKeyInferrer([], clickUpNative: true);
        Assert.Empty(inf.Extract(text));
    }

    [Fact]
    public void Extract_native_clickup_bypasses_allowlist()
    {
        var inf = new TicketKeyInferrer(["SFTY"], clickUpNative: true);
        Assert.Equal(["CU-86b1abcde"], inf.Extract("CU-86b1abcde and QS-1").ToList());
    }
}
