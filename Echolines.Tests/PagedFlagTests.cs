using Echolines.Server.Data.Entities;
using Echolines.Server.Models;
using Echolines.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Tests;

/// <summary>
/// The "shown over several Talk windows" flag. Sticky, caught up on lines the server already knows,
/// and never a reason to treat a report as "already known, nothing to do".
/// </summary>
public class PagedFlagTests
{
    private static SubmitLine Paged(SubmitLine line)
    {
        line.Paged = true;
        return line;
    }

    private static async Task PromoteVia(IngestTestHarness h, LineOrigin origin, bool paged = false)
    {
        foreach (var install in new[] { IngestTestHarness.InstallA, IngestTestHarness.InstallB, IngestTestHarness.InstallC })
        {
            var line = IngestTestHarness.Line();
            await h.Submit(install, origin, paged ? Paged(line) : line);
        }
    }

    [Fact]
    public async Task Known_line_reported_with_the_flag_gets_it_caught_up()
    {
        using var h = new IngestTestHarness();
        await PromoteVia(h, LineOrigin.Harvest);

        // Same origin, same installation: without the flag this would be a pure read.
        var result = await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(IngestTestHarness.Line()));

        Assert.Equal(1, result.AlreadyKnown);
        Assert.Equal(1, result.PagedUpdated);
        Assert.False(result.ReadOnly);

        await using var db = h.NewContext();
        Assert.True((await db.Lines.SingleAsync()).Paged);
    }

    [Fact]
    public async Task Flag_is_sticky_a_later_report_without_it_does_not_clear_it()
    {
        using var h = new IngestTestHarness();
        await PromoteVia(h, LineOrigin.Harvest, paged: true);

        var result = await h.SubmitLive("44444444-4444-4444-4444-444444444444", IngestTestHarness.Line());

        Assert.Equal(0, result.PagedUpdated);
        await using var db = h.NewContext();
        Assert.True((await db.Lines.SingleAsync()).Paged);
    }

    [Fact]
    public async Task Repeated_flagged_report_of_a_flagged_line_stays_read_only()
    {
        using var h = new IngestTestHarness();
        await PromoteVia(h, LineOrigin.Harvest, paged: true);

        var result = await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(IngestTestHarness.Line()));

        Assert.Equal(0, result.PagedUpdated);
        Assert.True(result.ReadOnly);
    }

    [Fact]
    public async Task Pending_line_gets_the_flag_from_an_installation_that_already_voted()
    {
        using var h = new IngestTestHarness();
        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());

        var result = await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(IngestTestHarness.Line()));

        Assert.Equal(1, result.PagedUpdated);
        Assert.Equal(0, result.NewVotes);
        await using var db = h.NewContext();
        var pending = await db.PendingLines.SingleAsync();
        Assert.True(pending.Paged);
        Assert.Equal(1, pending.HitCount);
    }

    [Fact]
    public async Task One_flagged_vote_is_enough_and_promotion_carries_it()
    {
        using var h = new IngestTestHarness();
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(IngestTestHarness.Line()));
        await h.SubmitHarvest(IngestTestHarness.InstallB, IngestTestHarness.Line());
        var result = await h.SubmitHarvest(IngestTestHarness.InstallC, IngestTestHarness.Line());

        Assert.Equal(1, result.Promoted);
        await using var db = h.NewContext();
        Assert.True((await db.Lines.SingleAsync()).Paged);
    }

    [Fact]
    public async Task Flag_is_not_part_of_the_key()
    {
        using var h = new IngestTestHarness();
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(IngestTestHarness.Line()));
        await h.SubmitLive(IngestTestHarness.InstallB, IngestTestHarness.Line());

        await using var db = h.NewContext();
        var pending = await db.PendingLines.SingleAsync();
        Assert.Equal(2, pending.HitCount);
    }

    [Fact]
    public async Task Rejected_line_does_not_take_the_flag()
    {
        using var h = new IngestTestHarness();
        await h.SubmitHarvest(IngestTestHarness.InstallA, IngestTestHarness.Line());
        await using (var db = h.NewContext())
        {
            var pending = await db.PendingLines.SingleAsync();
            pending.State = PendingState.Rejected;
            await db.SaveChangesAsync();
        }

        var result = await h.SubmitHarvest(IngestTestHarness.InstallB, Paged(IngestTestHarness.Line()));

        Assert.Equal(1, result.DroppedRejected);
        Assert.Equal(0, result.PagedUpdated);
    }

    [Fact]
    public void MergeInto_sets_the_flag_but_never_clears_it()
    {
        var now = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        var line = new Line { Lang = "en", NpcName = "X", Text = "t", TextHash = "t", Paged = false };
        var flagged = new PendingLine { Lang = "en", NpcName = "X", Text = "t", TextHash = "t", Paged = true };

        Assert.True(LinePromotion.MergeInto(line, flagged, now));
        Assert.True(line.Paged);

        var unflagged = new PendingLine { Lang = "en", NpcName = "X", Text = "t", TextHash = "t", Paged = false };
        LinePromotion.MergeInto(line, unflagged, now);
        Assert.True(line.Paged);
    }
}
