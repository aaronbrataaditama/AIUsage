using AIUsage.Bridge.Handlers;

namespace AIUsage.Tests;

public class TicketHandlersTests
{
    [Fact]
    public void Tally_counts_each_outcome_bucket()
    {
        var outcomes = new[]
        {
            TicketHandlers.SyncOutcome.Ok,
            TicketHandlers.SyncOutcome.Skipped,
            TicketHandlers.SyncOutcome.Dead,
            TicketHandlers.SyncOutcome.Skipped,
        };

        var (ok, dead, failed, skipped) = TicketHandlers.Tally(outcomes);

        Assert.Equal(1, ok);
        Assert.Equal(1, dead);
        Assert.Equal(0, failed);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void ParsePrefixes_uppercases_and_normalizes()
    {
        Assert.Equal("DEV,OPS", SettingsHandlers.ParsePrefixes("dev, ops"));
    }

    [Fact]
    public void ParsePrefixes_rejects_a_non_key_shaped_token()
    {
        Assert.Throws<ArgumentException>(() => SettingsHandlers.ParsePrefixes("DEV,x y"));
    }
}
