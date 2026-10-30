using Echolines.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Tests;

/// <summary>Consensus and promotion — the plan's first block of test cases.</summary>
public class ConsensusTests
{
    [Fact]
    public async Task Same_installation_reporting_ten_times_yields_one_vote_and_no_promotion()
    {
        using var h = new IngestTestHarness();

        for (var i = 0; i < 10; i++)
            await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());

        await using var db = h.NewContext();
        Assert.Empty(db.Lines);
        var pending = await db.PendingLines.Include(p => p.Votes).SingleAsync();
        Assert.Single(pending.Votes);
        Assert.Equal(1, pending.HitCount);
        Assert.Equal(PendingState.Pending, pending.State);
    }

    [Fact]
    public async Task Promotion_happens_on_the_third_distinct_installation_not_the_second()
    {
        using var h = new IngestTestHarness();

        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());
        await h.SubmitLive(IngestTestHarness.InstallB, IngestTestHarness.Line());

        await using (var afterTwo = h.NewContext())
            Assert.Empty(afterTwo.Lines);

        var third = await h.SubmitLive(IngestTestHarness.InstallC, IngestTestHarness.Line());
        Assert.Equal(1, third.Promoted);

        await using var db = h.NewContext();
        var line = await db.Lines.SingleAsync();
        Assert.Equal(3, line.SourceCount);
        Assert.True(line.SeenLive);
        Assert.False(line.SeenHarvest);
        Assert.NotNull(line.PromotedUtc);
    }

    [Fact]
    public async Task Promotion_marks_the_pending_row_instead_of_deleting_it_and_keeps_the_votes()
    {
        using var h = new IngestTestHarness();

        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());
        await h.SubmitHarvest(IngestTestHarness.InstallB, IngestTestHarness.Line());
        await h.SubmitLive(IngestTestHarness.InstallC, IngestTestHarness.Line());

        await using var db = h.NewContext();
        var pending = await db.PendingLines.Include(p => p.Votes).SingleAsync();
        Assert.Equal(PendingState.Promoted, pending.State);
        Assert.Equal(3, pending.Votes.Count);
        Assert.Equal(2, pending.Votes.Count(v => v.Origin == LineOrigin.Live));
        Assert.Equal(1, pending.Votes.Count(v => v.Origin == LineOrigin.Harvest));

        // Origins are rebuilt from the votes at promotion time, not from whoever happened to be last.
        var line = await db.Lines.SingleAsync();
        Assert.True(line.SeenLive);
        Assert.True(line.SeenHarvest);
    }

    [Fact]
    public async Task Rejected_entry_swallows_further_votes_and_is_never_promoted_again()
    {
        using var h = new IngestTestHarness();

        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());

        await using (var db = h.NewContext())
        {
            var pending = await db.PendingLines.SingleAsync();
            pending.State = PendingState.Rejected;
            await db.SaveChangesAsync();
        }

        var second = await h.SubmitLive(IngestTestHarness.InstallB, IngestTestHarness.Line());
        var third = await h.SubmitLive(IngestTestHarness.InstallC, IngestTestHarness.Line());

        Assert.Equal(1, second.DroppedRejected);
        Assert.Equal(1, third.DroppedRejected);
        Assert.Equal(0, third.Promoted);

        await using var check = h.NewContext();
        Assert.Empty(check.Lines);
        var stillRejected = await check.PendingLines.Include(p => p.Votes).SingleAsync();
        Assert.Equal(PendingState.Rejected, stillRejected.State);
        Assert.Single(stillRejected.Votes);
    }

    [Fact]
    public async Task Duplicate_lines_inside_one_payload_create_one_pending_row_and_one_vote()
    {
        using var h = new IngestTestHarness();

        // A client that re-sends a chunk after a network hiccup, or a harvest with the same line twice.
        await h.SubmitLive(
            IngestTestHarness.InstallA,
            IngestTestHarness.Line(),
            IngestTestHarness.Line(),
            IngestTestHarness.Line());

        await using var db = h.NewContext();
        var pending = await db.PendingLines.Include(p => p.Votes).SingleAsync();
        Assert.Single(pending.Votes);
        Assert.Equal(1, pending.HitCount);
    }
}
