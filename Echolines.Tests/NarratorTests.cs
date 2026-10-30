using System.Text;
using System.Text.Json;
using Echolines.Server.Data.Entities;
using Echolines.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Echolines.Tests;

/// <summary>
/// Narrator dialogue — quest text with no speaker.
/// </summary>
/// <remarks>
/// This is not an edge case: SYSTEM, QIB and POPMESSAGE entries are a large share of the game's quest
/// text, and Echokraut's live path reports them with an empty speaker
/// (<c>AddonTalkHelper</c>: <c>state.Speaker ?? ""</c>). Refusing an empty name threw all of it away.
/// </remarks>
public class NarratorTests
{
    [Fact]
    public async Task A_line_with_no_speaker_is_stored_as_the_narrator_instead_of_being_dropped()
    {
        using var h = new IngestTestHarness();

        var line = IngestTestHarness.Line("The gates of Ul'dah stand before you.");
        line.Npc = "";

        var result = await h.SubmitLive(IngestTestHarness.InstallA, line);

        Assert.Equal(0, result.DroppedInvalid);
        Assert.Equal(1, result.NewPending);

        await using var db = h.NewContext();
        Assert.Equal(NpcName.Narrator, (await db.PendingLines.SingleAsync()).NpcName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SYSTEM")]
    [InlineData("Narrator")]
    [InlineData("Erzähler")]
    [InlineData("Narrateur")]
    [InlineData("ナレーター")]
    public void Every_spelling_of_the_narrator_folds_onto_one_key(string? npc) =>
        Assert.Equal(NpcName.Narrator, NpcName.Canonicalise(npc));

    [Fact]
    public async Task The_live_and_harvest_spellings_gather_votes_on_the_same_entry()
    {
        // The point of folding them: the live path sends "", the harvest sends "Narrator". Kept apart,
        // the same line would sit in the database twice and neither copy would ever reach three votes.
        using var h = new IngestTestHarness();

        const string text = "The gates of Ul'dah stand before you.";

        var live = IngestTestHarness.Line(text);
        live.Npc = "";
        var harvest = IngestTestHarness.Line(text, npc: "Narrator");

        await h.SubmitLive(IngestTestHarness.InstallA, live);
        await h.SubmitHarvest(IngestTestHarness.InstallB, harvest);
        var third = await h.SubmitLive(IngestTestHarness.InstallC, live);

        Assert.Equal(1, third.Promoted);

        await using var db = h.NewContext();
        var promoted = await db.Lines.SingleAsync();
        Assert.Equal(NpcName.Narrator, promoted.NpcName);
        Assert.Equal(3, promoted.SourceCount);
        Assert.True(promoted.SeenLive);
        Assert.True(promoted.SeenHarvest);
    }

    [Fact]
    public async Task A_real_speaker_is_left_alone()
    {
        using var h = new IngestTestHarness();

        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line(npc: "Alphinaud Leveilleur"));

        await using var db = h.NewContext();
        Assert.Equal("Alphinaud Leveilleur", (await db.PendingLines.SingleAsync()).NpcName);
    }

    [Fact]
    public async Task A_Drive_file_with_an_empty_name_segment_imports_as_the_narrator()
    {
        using var h = new IngestTestHarness();

        // What the old path actually wrote for a narrator line: nothing between the race and the slug.
        const string text = "The gates of Ul'dah stand before you.";
        var name = $"English_None_Unknown__{LineKey.FromText(text)}.json";
        var json = JsonSerializer.Serialize(new { Gender = 0, Race = 0, Language = 1, Name = "", Text = text });
        var file = new DriveImportItem(name, new MemoryStream(Encoding.UTF8.GetBytes(json)));

        await using var db = h.NewContext();
        var report = await new DriveImportService(db, h.Clock, NullLogger<DriveImportService>.Instance)
            .ImportAsync([file]);

        Assert.Empty(report.Conflicts);
        Assert.Equal(1, report.Imported);

        var stored = await db.Lines.SingleAsync();
        Assert.Equal(NpcName.Narrator, stored.NpcName);
        Assert.Equal(text, stored.Text);
    }
}
