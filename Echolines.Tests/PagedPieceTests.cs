using System.Text;
using System.Text.Json;
using Echolines.Server.Data.Entities;
using Echolines.Server.Models;
using Echolines.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Echolines.Tests;

/// <summary>
/// Window pieces of a paged line: the live path reports each Talk window, the harvest the whole row.
/// The pieces must not read as harvest gap, and the whole row's missing live vote must not read as
/// dead content. Runs on SQLite so the substring translation is actually exercised.
/// </summary>
public class PagedPieceTests
{
    private const string Window1 = "Wir müssen uns auf den Weg machen, bevor die Sonne untergeht.";
    private const string Window2 = "Die Vorbereitungen sind fast abgeschlossen, <PLAYERFIRSTNAME>.";
    private const string Whole = Window1 + " " + Window2;
    private const string Npc = "Urianger";

    private static SubmitLine Paged(SubmitLine line)
    {
        line.Paged = true;
        return line;
    }

    private static SubmitLine Line(string text, string lang = "de", string npc = Npc) =>
        IngestTestHarness.Line(text, lang, npc);

    private static async Task<Line> Stored(IngestTestHarness h, string text)
    {
        await using var db = h.NewContext();
        return await db.Lines.SingleAsync(l => l.Text == text);
    }

    private static async Task<InsightCounts> Counts(IngestTestHarness h)
    {
        await using var db = h.NewContext();
        return await new LineReviewService(db, h.Clock, new PagedPieceMarker(db)).GetCountsAsync();
    }

    private static async Task<IReadOnlyList<Line>> View(IngestTestHarness h, LineView view)
    {
        await using var db = h.NewContext();
        return (await new LineReviewService(db, h.Clock, new PagedPieceMarker(db))
            .QueryLinesAsync(new LineFilter { View = view })).Items;
    }

    [Fact]
    public async Task Window_seen_live_after_the_whole_row_was_harvested_is_a_piece()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));

        await h.SubmitLive(IngestTestHarness.InstallB, Line(Window2));

        Assert.True((await Stored(h, Window2)).PagedPiece);
        Assert.False((await Stored(h, Whole)).PagedPiece);
    }

    [Fact]
    public async Task Window_seen_live_before_the_whole_row_arrived_is_marked_when_it_does()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitLive(IngestTestHarness.InstallB, Line(Window1));
        Assert.False((await Stored(h, Window1)).PagedPiece);

        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));

        Assert.True((await Stored(h, Window1)).PagedPiece);
    }

    [Fact]
    public async Task Known_row_that_is_flagged_paged_later_catches_up_its_pieces()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Line(Whole)); // an older client: no flag
        await h.SubmitLive(IngestTestHarness.InstallB, Line(Window1));
        Assert.False((await Stored(h, Window1)).PagedPiece);

        var result = await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));

        Assert.Equal(1, result.PagedUpdated);
        Assert.True((await Stored(h, Window1)).PagedPiece);
    }

    [Theory]
    [InlineData("de", "Y'shtola")] // another NPC saying the same words
    [InlineData("en", Npc)] // the same NPC in another language
    public async Task Piece_needs_the_same_language_and_the_same_npc(string lang, string npc)
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));

        await h.SubmitLive(IngestTestHarness.InstallB, Line(Window1, lang, npc));

        Assert.False((await Stored(h, Window1)).PagedPiece);
    }

    [Fact]
    public async Task Text_that_is_not_part_of_the_row_stays_a_harvest_gap()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));

        await h.SubmitLive(IngestTestHarness.InstallB, Line("Das hat mit der Zeile nichts zu tun."));

        var gap = Assert.Single(await View(h, LineView.HarvestGap));
        Assert.False(gap.PagedPiece);
    }

    [Fact]
    public async Task Word_hyphenated_at_a_window_line_break_still_matches()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));

        // What live play actually delivers when the window wraps inside a word.
        const string wrapped = "Die Vor- bereitungen sind fast abgeschlossen, <PLAYERFIRSTNAME>.";
        await h.SubmitLive(IngestTestHarness.InstallB, Line(wrapped));

        Assert.True((await Stored(h, wrapped)).PagedPiece);
    }

    [Fact]
    public async Task Views_and_counts_explain_both_sides_instead_of_reporting_them()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));
        await h.SubmitLive(IngestTestHarness.InstallB, Line(Window1), Line(Window2));
        await h.SubmitLive(IngestTestHarness.InstallB, Line("Ein echter Fund, den kein Harvest kennt."));
        await h.SubmitHarvest(IngestTestHarness.InstallA, Line("Tote Zeile, nie gesprochen."));

        var counts = await Counts(h);
        Assert.Equal(1, counts.HarvestGap);
        Assert.Equal(2, counts.PagedPieces);
        Assert.Equal(1, counts.NeverEncountered);
        Assert.Equal(1, counts.PagedNotLive);

        Assert.Equal("Ein echter Fund, den kein Harvest kennt.", Assert.Single(await View(h, LineView.HarvestGap)).Text);
        Assert.Equal("Tote Zeile, nie gesprochen.", Assert.Single(await View(h, LineView.NeverEncountered)).Text);
        Assert.Equal(Whole, Assert.Single(await View(h, LineView.PagedNotLive)).Text);
        Assert.Equal(2, (await View(h, LineView.PagedPieces)).Count);
    }

    [Fact]
    public async Task Paged_row_seen_live_as_a_whole_is_neither_unmet_nor_a_gap()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));
        // A client that joins the windows reports the whole row too.
        await h.SubmitLive(IngestTestHarness.InstallB, Paged(Line(Whole)));

        var counts = await Counts(h);
        Assert.Equal(0, counts.NeverEncountered);
        Assert.Equal(0, counts.PagedNotLive);
        Assert.Equal(0, counts.HarvestGap);
    }

    [Fact]
    public async Task Manual_release_of_a_piece_marks_it()
    {
        using var h = new IngestTestHarness(); // threshold 3: one vote stays in quarantine
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole)));
        await h.SubmitHarvest(IngestTestHarness.InstallB, Paged(Line(Whole)));
        await h.SubmitHarvest(IngestTestHarness.InstallC, Paged(Line(Whole)));
        await h.SubmitLive(IngestTestHarness.InstallA, Line(Window2));

        await using (var db = h.NewContext())
        {
            var id = (await db.PendingLines.SingleAsync(p => p.Text == Window2)).Id;
            Assert.Equal(1, await new LineReviewService(db, h.Clock, new PagedPieceMarker(db)).ApproveAsync([id]));
        }

        Assert.True((await Stored(h, Window2)).PagedPiece);
    }

    [Fact]
    public async Task Drive_import_marks_pieces_of_a_known_paged_row()
    {
        using var h = new IngestTestHarness(promotionThreshold: 1);
        await h.SubmitHarvest(IngestTestHarness.InstallA, Paged(Line(Whole, "en", Npc)));

        const string text = "Wir müssen uns auf den Weg machen, bevor die Sonne untergeht.";
        var name = $"English_Male_Elezen_{Npc}_{LineKey.FromText(text)}.json";
        var json = JsonSerializer.Serialize(new { Gender = 0, Race = 0, Language = 1, Name = Npc, Text = text });

        await using (var db = h.NewContext())
        {
            var report = await new DriveImportService(db, h.Clock, new PagedPieceMarker(db), NullLogger<DriveImportService>.Instance)
                .ImportAsync([new DriveImportItem(name, new MemoryStream(Encoding.UTF8.GetBytes(json)))]);
            Assert.Equal(1, report.Imported);
        }

        Assert.True((await Stored(h, text)).PagedPiece);
    }

    [Fact]
    public async Task Marker_with_nothing_to_do_returns_zero()
    {
        using var h = new IngestTestHarness();
        await using var db = h.NewContext();
        Assert.Equal(0, await new PagedPieceMarker(db).MarkAsync([]));
    }
}
