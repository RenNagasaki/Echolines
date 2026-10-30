using Echolines.Server.Data.Entities;
using Echolines.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Tests;

/// <summary>Phase 4 (manual release below the threshold) and Phase 6 (the origin views).</summary>
public class ReviewTests
{
    private static LineReviewService Review(IngestTestHarness h, out Echolines.Server.Data.EcholinesDbContext db)
    {
        db = h.NewContext();
        return new LineReviewService(db, h.Clock);
    }

    [Fact]
    public async Task Operator_can_release_a_line_that_only_one_installation_ever_reported()
    {
        using var h = new IngestTestHarness();
        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());

        int id;
        await using (var db = h.NewContext())
            id = (await db.PendingLines.SingleAsync()).Id;

        await using (var db = h.NewContext())
            Assert.Equal(1, await new LineReviewService(db, h.Clock).ApproveAsync([id]));

        await using var check = h.NewContext();
        var line = await check.Lines.SingleAsync();
        Assert.Equal(1, line.SourceCount);
        Assert.True(line.SeenLive);
        Assert.False(line.OriginOperator);
        Assert.Equal(PendingState.Promoted, (await check.PendingLines.SingleAsync()).State);
    }

    [Fact]
    public async Task Turning_an_entry_down_keeps_the_row_and_takes_it_out_of_the_default_view()
    {
        using var h = new IngestTestHarness();
        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());

        int id;
        await using (var db = h.NewContext())
            id = (await db.PendingLines.SingleAsync()).Id;

        await using (var db = h.NewContext())
            Assert.Equal(1, await new LineReviewService(db, h.Clock).RejectAsync([id]));

        await using var check = h.NewContext();
        var review = new LineReviewService(check, h.Clock);

        Assert.Empty((await review.QueryPendingAsync(new PendingFilter())).Items);
        Assert.Single((await review.QueryPendingAsync(new PendingFilter { State = PendingState.Rejected })).Items);
        Assert.Equal(1, await check.PendingLines.CountAsync());
    }

    [Fact]
    public async Task Releasing_something_the_Drive_import_already_holds_merges_instead_of_failing()
    {
        using var h = new IngestTestHarness();
        var submitted = IngestTestHarness.Line();
        await h.SubmitLive(IngestTestHarness.InstallA, submitted);

        // The operator imported the same line from the old Drive stock in the meantime. The unique index
        // would reject a second insert, so the release has to fold the vote into the existing row.
        int id;
        await using (var db = h.NewContext())
        {
            db.Lines.Add(new Line
            {
                Lang = "en",
                NpcName = "Alphinaud Leveilleur",
                Text = submitted.Text!,
                TextHash = LineKey.FromText(submitted.Text!),
                FirstSeenUtc = h.Clock.GetUtcNow().UtcDateTime,
                LastSeenUtc = h.Clock.GetUtcNow().UtcDateTime,
                OriginOperator = true,
            });
            await db.SaveChangesAsync();
            id = (await db.PendingLines.SingleAsync()).Id;
        }

        await using (var db = h.NewContext())
            await new LineReviewService(db, h.Clock).ApproveAsync([id]);

        await using var check = h.NewContext();
        var line = await check.Lines.SingleAsync();
        Assert.True(line.SeenLive);
        Assert.True(line.OriginOperator);
        Assert.Equal(1, line.SourceCount);
    }

    [Fact]
    public async Task Releasing_an_already_decided_entry_does_nothing()
    {
        using var h = new IngestTestHarness();
        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line());

        int id;
        await using (var db = h.NewContext())
            id = (await db.PendingLines.SingleAsync()).Id;

        await using (var db = h.NewContext())
            await new LineReviewService(db, h.Clock).RejectAsync([id]);

        await using (var db = h.NewContext())
            Assert.Equal(0, await new LineReviewService(db, h.Clock).ApproveAsync([id]));

        await using var check = h.NewContext();
        Assert.Empty(check.Lines);
    }

    [Fact]
    public async Task Harvest_gap_view_shows_exactly_the_lines_no_harvest_has_delivered()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);

        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line("Only a player has heard this."));
        await h.SubmitHarvest(IngestTestHarness.InstallA, IngestTestHarness.Line("Only the game files know this."));

        await using var db = h.NewContext();
        var review = new LineReviewService(db, h.Clock);

        var gap = await review.QueryLinesAsync(new LineFilter { View = LineView.HarvestGap });
        Assert.Equal("Only a player has heard this.", Assert.Single(gap.Items).Text);

        var unseen = await review.QueryLinesAsync(new LineFilter { View = LineView.NeverEncountered });
        Assert.Equal("Only the game files know this.", Assert.Single(unseen.Items).Text);

        var counts = await review.GetCountsAsync();
        Assert.Equal(2, counts.Lines);
        Assert.Equal(1, counts.HarvestGap);
        Assert.Equal(1, counts.NeverEncountered);
        Assert.Equal(0, counts.OperatorOnly);
    }

    [Fact]
    public async Task Paging_covers_every_entry_exactly_once()
    {
        using var h = new IngestTestHarness();

        for (var i = 0; i < 25; i++)
            await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line($"Line number {i}."));

        await using var db = h.NewContext();
        var review = new LineReviewService(db, h.Clock);

        var seen = new HashSet<int>();
        var first = await review.QueryPendingAsync(new PendingFilter { PageSize = 10 });
        Assert.Equal(25, first.Total);
        Assert.Equal(3, first.PageCount);

        for (var page = 0; page < first.PageCount; page++)
        {
            var result = await review.QueryPendingAsync(new PendingFilter { PageSize = 10, PageIndex = page });
            foreach (var item in result.Items)
                Assert.True(seen.Add(item.Id), "an entry showed up on two pages");
        }

        Assert.Equal(25, seen.Count);
    }

    [Fact]
    public async Task Release_all_moves_every_pending_entry_across_more_than_one_batch()
    {
        using var h = new IngestTestHarness();
        var total = LineReviewService.ApproveAllBatchSize * 2 + 500;

        for (var start = 0; start < total; start += 1000)
            await h.SubmitLive(IngestTestHarness.InstallA,
                [.. Enumerable.Range(start, Math.Min(1000, total - start)).Select(i => IngestTestHarness.Line($"Line number {i}."))]);

        var reported = new List<int>();
        await using (var db = h.NewContext())
            Assert.Equal(total, await new LineReviewService(db, h.Clock).ApproveAllAsync(new PendingFilter(), reported.Add));

        Assert.Equal([LineReviewService.ApproveAllBatchSize, LineReviewService.ApproveAllBatchSize * 2, total], reported);

        await using var check = h.NewContext();
        Assert.Equal(total, await check.Lines.CountAsync());
        Assert.False(await check.PendingLines.AnyAsync(p => p.State != PendingState.Promoted));
        Assert.True(await check.Lines.AllAsync(l => l.SeenLive && l.SourceCount == 1));
    }

    [Fact]
    public async Task Release_all_only_touches_what_the_filter_matches()
    {
        using var h = new IngestTestHarness();
        await h.SubmitLive(IngestTestHarness.InstallA,
            IngestTestHarness.Line("English one."),
            IngestTestHarness.Line("English two."),
            IngestTestHarness.Line("Deutsch eins.", lang: "de"));

        await using (var db = h.NewContext())
            Assert.Equal(2, await new LineReviewService(db, h.Clock).ApproveAllAsync(new PendingFilter { Lang = "en" }));

        await using var check = h.NewContext();
        Assert.All(await check.Lines.ToListAsync(), l => Assert.Equal("en", l.Lang));
        Assert.Equal(PendingState.Pending, (await check.PendingLines.SingleAsync(p => p.Lang == "de")).State);
    }

    [Fact]
    public async Task Release_all_leaves_turned_down_entries_alone_even_from_the_rejected_view()
    {
        using var h = new IngestTestHarness();
        await h.SubmitLive(IngestTestHarness.InstallA,
            IngestTestHarness.Line("Keep me out."),
            IngestTestHarness.Line("Let me in."));

        await using (var db = h.NewContext())
        {
            var rejected = (await db.PendingLines.SingleAsync(p => p.Text == "Keep me out.")).Id;
            await new LineReviewService(db, h.Clock).RejectAsync([rejected]);
        }

        // The page passes whatever state it is showing; the service must still only release pending ones.
        await using (var db = h.NewContext())
            Assert.Equal(1, await new LineReviewService(db, h.Clock).ApproveAllAsync(new PendingFilter { State = PendingState.Rejected }));

        await using var check = h.NewContext();
        Assert.Equal("Let me in.", (await check.Lines.SingleAsync()).Text);
        Assert.Equal(PendingState.Rejected, (await check.PendingLines.SingleAsync(p => p.Text == "Keep me out.")).State);
    }

    [Fact]
    public async Task Release_all_merges_into_lines_the_Drive_import_already_holds()
    {
        using var h = new IngestTestHarness();
        var submitted = IngestTestHarness.Line();
        await h.SubmitLive(IngestTestHarness.InstallA, submitted);

        await using (var db = h.NewContext())
        {
            db.Lines.Add(new Line
            {
                Lang = "en",
                NpcName = "Alphinaud Leveilleur",
                Text = submitted.Text!,
                TextHash = LineKey.FromText(submitted.Text!),
                FirstSeenUtc = h.Clock.GetUtcNow().UtcDateTime,
                LastSeenUtc = h.Clock.GetUtcNow().UtcDateTime,
                OriginOperator = true,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = h.NewContext())
            Assert.Equal(1, await new LineReviewService(db, h.Clock).ApproveAllAsync(new PendingFilter()));

        await using var check = h.NewContext();
        var line = await check.Lines.SingleAsync();
        Assert.True(line.SeenLive);
        Assert.True(line.OriginOperator);
    }
}
