using AIUsage.Settings;

namespace AIUsage.Tests;

public class SettingsAllowlistTests
{
    [Fact]
    public void EffectiveAllowlist_empty_stays_empty_so_everything_is_allowed() =>
        Assert.Empty(SettingsStore.CombineAllowlist("", "DEV,OPS"));

    [Fact]
    public void EffectiveAllowlist_unions_clickup_prefixes_when_restricted() =>
        Assert.Equal(new HashSet<string> { "SFTY", "DEV", "OPS" },
            SettingsStore.CombineAllowlist("sfty", " dev , OPS "));
}
