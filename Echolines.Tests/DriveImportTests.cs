using System.Text;
using System.Text.Json;
using Echolines.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Echolines.Tests;

/// <summary>Phase 5 — the bulk import of the old Google Drive stock.</summary>
public class DriveImportTests
{
    private static DriveImportItem File(string fileName, string text)
    {
        // Mirrors what the plugin actually wrote: JsonSerializer.Serialize(voiceLine) with the enums
        // as bare numbers, because no string converter was in that path.
        var json = JsonSerializer.Serialize(new { Gender = 1, Race = 3, Language = 1, Name = "Alphinaud", Text = text });
        return new DriveImportItem(fileName, new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private static string NameFor(string text, string language = "English", string npc = "Alphinaud") =>
        $"{language}_Male_Elezen_{npc}_{LineKey.FromText(text)}.json";

    private static DriveImportService Service(IngestTestHarness h, Echolines.Server.Data.EcholinesDbContext db) =>
        new(db, h.Clock, NullLogger<DriveImportService>.Instance);

    [Fact]
    public async Task Imported_lines_go_straight_into_the_main_table_marked_as_operator_origin()
    {
        using var h = new IngestTestHarness();
        const string text = "Well met, adventurer.";

        await using var db = h.NewContext();
        var report = await Service(h, db).ImportAsync([File(NameFor(text), text)]);

        Assert.Equal(1, report.Imported);
        Assert.Empty(report.Conflicts);

        var line = await db.Lines.SingleAsync();
        Assert.True(line.OriginOperator);
        // Neither live nor harvest: nothing is known about this line beyond the operator having it.
        Assert.False(line.SeenLive);
        Assert.False(line.SeenHarvest);
        Assert.Equal("en", line.Lang);
        Assert.Equal("Alphinaud", line.NpcName);
        Assert.Equal(0, line.SourceCount);

        Assert.Empty(db.PendingLines);
    }

    [Fact]
    public async Task The_file_name_wins_over_the_body_for_language_gender_and_race()
    {
        using var h = new IngestTestHarness();
        const string text = "Bonjour.";

        // Body says Language = 1 (English in the plugin's enum); the name says German. The name is the
        // one written as plain text and survives an enum reorder, so the name decides.
        await using var db = h.NewContext();
        await Service(h, db).ImportAsync([File(NameFor(text, language: "German"), text)]);

        Assert.Equal("de", (await db.Lines.SingleAsync()).Lang);
    }

    [Fact]
    public async Task A_body_that_still_holds_a_real_character_name_is_reported_not_imported()
    {
        using var h = new IngestTestHarness();

        // The plugin tokenised the player name for the file-name slug only — the serialised text kept it.
        // This is the case that would otherwise put a real player's name into a public database.
        var name = NameFor("Well met, <PLAYERFIRSTNAME>.");
        var body = File(name, "Well met, Cloud.");

        await using var db = h.NewContext();
        var report = await Service(h, db).ImportAsync([body]);

        Assert.Equal(0, report.Imported);
        var conflict = Assert.Single(report.Conflicts);
        Assert.Contains("does not reproduce the slug", conflict.Reason);
        Assert.Empty(db.Lines);
    }

    [Fact]
    public async Task Punctuation_is_forgiven_but_a_different_wording_is_not()
    {
        // The tolerance added for substituted characters must not become tolerance for a different
        // line. Only punctuation is ignored when matching; letters still have to agree.
        using var h = new IngestTestHarness();
        var name = NameFor("Well met, adventurer.");

        await using var db = h.NewContext();
        var report = await Service(h, db).ImportAsync([File(name, "Well met, traveller.")]);

        Assert.Equal(0, report.Imported);
        Assert.Single(report.Conflicts);
    }

    [Fact]
    public async Task Re_importing_the_same_stock_reports_it_as_known_rather_than_duplicating_it()
    {
        using var h = new IngestTestHarness();
        const string text = "Well met, adventurer.";

        await using var db = h.NewContext();
        var service = Service(h, db);

        await service.ImportAsync([File(NameFor(text), text)]);
        var second = await service.ImportAsync([File(NameFor(text), text)]);

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.AlreadyKnown);
        Assert.Equal(1, await db.Lines.CountAsync());
    }

    [Fact]
    public async Task Duplicates_inside_one_selection_are_only_imported_once()
    {
        using var h = new IngestTestHarness();
        const string text = "Well met, adventurer.";

        await using var db = h.NewContext();
        var report = await Service(h, db).ImportAsync(
            [File(NameFor(text), text), File(NameFor(text), text)]);

        Assert.Equal(1, report.Imported);
        Assert.Equal(1, report.AlreadyKnown);
        Assert.Equal(1, await db.Lines.CountAsync());
    }

    [Fact]
    public async Task A_stored_name_whose_apostrophe_became_an_underscore_still_imports()
    {
        using var h = new IngestTestHarness();

        // Verbatim from the real Drive stock. The plugin's slug keeps the apostrophe — it is a legal
        // file-name character on Windows — but the stored name has an underscore in its place, so the
        // slug contains the one character the plugin swore it never would. Rejecting these throws away
        // good material, and splitting on the last underscore also drags half the slug into the NPC name.
        const string text = "Oh, you are Lyse's friend. Are you here to help us too?";
        const string name =
            "English_Female_AuRa_Bright-eyed Mol Girl_ohyouarelyse_sfriendareyouheretohelpustoo.json";

        await using var db = h.NewContext();
        var report = await Service(h, db).ImportAsync([File(name, text)]);

        Assert.Empty(report.Conflicts);
        Assert.Equal(1, report.Imported);

        var line = await db.Lines.SingleAsync();
        Assert.Equal("Bright-eyed Mol Girl", line.NpcName);
        Assert.Equal("Female", line.Gender);
        Assert.Equal("AuRa", line.Race);
        Assert.Equal(text, line.Text);
        // The key stays the canonical one, with the apostrophe the plugin would have produced.
        Assert.Equal(LineKey.FromText(text), line.TextHash);
    }

    [Fact]
    public async Task An_npc_name_containing_an_underscore_survives_the_file_name_parse()
    {
        using var h = new IngestTestHarness();
        const string text = "Hmm.";

        await using var db = h.NewContext();
        await Service(h, db).ImportAsync([File(NameFor(text, npc: "Wandering_Minstrel"), text)]);

        Assert.Equal("Wandering_Minstrel", (await db.Lines.SingleAsync()).NpcName);
    }

    [Theory]
    [InlineData("not-a-line.txt", "not a .json file")]
    [InlineData("English_Male_Elezen.json", "shape")]
    [InlineData("Klingon_Male_Elezen_Alphinaud_hmm.json", "unknown language")]
    public async Task Unusable_file_names_are_reported_with_a_reason(string fileName, string reasonFragment)
    {
        using var h = new IngestTestHarness();

        await using var db = h.NewContext();
        var report = await Service(h, db).ImportAsync([File(fileName, "Hmm.")]);

        Assert.Equal(0, report.Imported);
        Assert.Contains(reasonFragment, Assert.Single(report.Conflicts).Reason);
    }

    [Fact]
    public async Task Broken_json_is_reported_rather_than_throwing()
    {
        using var h = new IngestTestHarness();
        var broken = new DriveImportItem(
            NameFor("Hmm."),
            new MemoryStream(Encoding.UTF8.GetBytes("{ this is not json")));

        await using var db = h.NewContext();
        var report = await Service(h, db).ImportAsync([broken]);

        Assert.Contains("unreadable JSON", Assert.Single(report.Conflicts).Reason);
    }
}
