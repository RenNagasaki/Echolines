using Echolines.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Tests;

/// <summary>
/// Origin tracking — the block the plan calls "the case that tipped the design over once". If these
/// pass, Phase 6 ("what does the harvest not know?") stays answerable; if they rot, it silently
/// reports gaps that closed months ago.
/// </summary>
public class OriginTests
{
    private static async Task PromoteVia(IngestTestHarness h, LineOrigin origin)
    {
        await h.Submit(IngestTestHarness.InstallA, origin, IngestTestHarness.Line());
        await h.Submit(IngestTestHarness.InstallB, origin, IngestTestHarness.Line());
        await h.Submit(IngestTestHarness.InstallC, origin, IngestTestHarness.Line());
    }

    [Fact]
    public async Task Line_promoted_by_harvests_gets_SeenLive_caught_up_by_a_later_live_report()
    {
        using var h = new IngestTestHarness();
        await PromoteVia(h, LineOrigin.Harvest);

        await using (var before = h.NewContext())
        {
            var line = await before.Lines.SingleAsync();
            Assert.True(line.SeenHarvest);
            Assert.False(line.SeenLive);
        }

        var result = await h.SubmitLive("44444444-4444-4444-4444-444444444444", IngestTestHarness.Line());
        Assert.Equal(1, result.AlreadyKnown);
        Assert.Equal(1, result.OriginUpdated);

        await using var db = h.NewContext();
        var updated = await db.Lines.SingleAsync();
        Assert.True(updated.SeenLive);
        Assert.True(updated.SeenHarvest);
    }

    [Fact]
    public async Task Line_promoted_live_gets_SeenHarvest_caught_up_by_a_later_harvest()
    {
        using var h = new IngestTestHarness();
        await PromoteVia(h, LineOrigin.Live);

        var result = await h.SubmitHarvest("44444444-4444-4444-4444-444444444444", IngestTestHarness.Line());
        Assert.Equal(1, result.OriginUpdated);

        await using var db = h.NewContext();
        var updated = await db.Lines.SingleAsync();
        Assert.True(updated.SeenLive);
        Assert.True(updated.SeenHarvest);
    }

    [Fact]
    public async Task Repeated_report_of_a_known_line_writes_nothing()
    {
        using var h = new IngestTestHarness();
        await PromoteVia(h, LineOrigin.Live);

        // Same origin, nothing new to learn — the overwhelmingly common case once a user has played
        // a while. It must stay a pure read, or every dialogue in the game becomes a write.
        var result = await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());

        Assert.Equal(1, result.AlreadyKnown);
        Assert.Equal(0, result.OriginUpdated);
        Assert.True(result.ReadOnly);
    }

    [Fact]
    public async Task LastSeen_is_only_advanced_once_the_resolution_has_passed()
    {
        using var h = new IngestTestHarness();
        await PromoteVia(h, LineOrigin.Live);

        DateTime atPromotion;
        await using (var db = h.NewContext())
            atPromotion = (await db.Lines.SingleAsync()).LastSeenUtc;

        h.Clock.Advance(TimeSpan.FromHours(2));
        Assert.True((await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line())).ReadOnly);

        await using (var db = h.NewContext())
            Assert.Equal(atPromotion, (await db.Lines.SingleAsync()).LastSeenUtc);

        h.Clock.Advance(TimeSpan.FromDays(2));
        Assert.False((await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line())).ReadOnly);

        await using (var db = h.NewContext())
            Assert.True((await db.Lines.SingleAsync()).LastSeenUtc > atPromotion);
    }
}
