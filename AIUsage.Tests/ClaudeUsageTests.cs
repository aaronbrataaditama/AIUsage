using AIUsage.Platform;

namespace AIUsage.Tests;

public class ClaudeUsageTests
{
    // Captured from a real Enterprise seat on 2026-09-29 (unrelated keys trimmed).
    private const string Enterprise = """
        {"five_hour":null,"seven_day":null,"seven_day_opus":null,"seven_day_sonnet":null,
         "nimbus_quill":{"utilization":0.0,"resets_at":null},
         "extra_usage":{"is_enabled":true,"monthly_limit":60000,"used_credits":22185.0,"utilization":36.975,
                        "currency":"USD","decimal_places":2,"spend_limit_reached":false}}
        """;

    private const string Pro = """
        {"five_hour":{"utilization":12.0,"resets_at":"2026-09-29T15:00:00+00:00"},
         "seven_day":{"utilization":40.5,"resets_at":"2026-10-03T00:00:00+00:00"},
         "seven_day_opus":null,"extra_usage":{"is_enabled":false}}
        """;

    [Fact]
    public void Parse_enterprise_spend_only()
    {
        var bar = Assert.Single(ClaudeUsage.Parse(Enterprise).Bars);
        Assert.Equal("MONTHLY LIMIT", bar.Label);
        Assert.Equal(36.975, bar.Pct, 3);
        Assert.Equal("$221.85 / $600.00", bar.Detail);
    }

    [Fact]
    public void Parse_pro_windows_unchanged()
    {
        var bars = ClaudeUsage.Parse(Pro).Bars;
        Assert.Equal(["SESSION", "WEEK"], bars.Select(b => b.Label).ToList());
        Assert.Equal(12.0, bars[0].Pct);
        Assert.NotNull(bars[1].ResetsAt);
    }

    [Fact]
    public void Parse_max_adds_per_model_week_and_extra_usage()
    {
        var json = """
            {"five_hour":{"utilization":5},"seven_day":{"utilization":10},
             "seven_day_opus":{"utilization":70},"seven_day_sonnet":{"utilization":20},
             "extra_usage":{"is_enabled":true,"monthly_limit":5000,"used_credits":1250,"utilization":25,
                            "currency":"EUR","decimal_places":2}}
            """;
        var labels = ClaudeUsage.Parse(json).Bars.Select(b => b.Label).ToList();
        Assert.Equal(["SESSION", "WEEK", "WEEK · OPUS", "WEEK · SONNET", "EXTRA USAGE"], labels);
        Assert.Equal("EUR 12.50 / EUR 50.00", ClaudeUsage.Parse(json).Bars[^1].Detail);
    }

    [Fact]
    public void Parse_spend_limit_reached_pins_to_100()
    {
        var json = Enterprise.Replace("\"spend_limit_reached\":false", "\"spend_limit_reached\":true");
        var bar = Assert.Single(ClaudeUsage.Parse(json).Bars);
        Assert.Equal(100, bar.Pct);
        Assert.Contains("limit reached", bar.Detail);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{not json")]
    [InlineData("""{"extra_usage":{"is_enabled":true,"monthly_limit":0}}""")]   // no cap → no bar
    public void Parse_degrades_to_no_bars(string json) => Assert.False(ClaudeUsage.Parse(json).HasAny);

    [Fact]
    public void Parse_ignores_null_extra_usage_fields_and_keeps_rolling_bars()
    {
        // Null (non-Number) extra_usage fields used to throw InvalidOperationException out of
        // JsonElement.TryGetDouble/TryGetInt32 mid-parse, which escaped the JsonException-only
        // catch and blanked the whole cached usage snapshot — losing the SESSION/WEEK bars too.
        var json = """
            {"five_hour":{"utilization":12.0,"resets_at":"2026-09-29T15:00:00+00:00"},
             "extra_usage":{"is_enabled":true,"monthly_limit":null,"utilization":null,"decimal_places":null}}
            """;
        var bars = ClaudeUsage.Parse(json).Bars;
        var bar = Assert.Single(bars);
        Assert.Equal("SESSION", bar.Label);
    }
}
