using Echolines.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Tests;

public class LanguageSeparationTests
{
    [Fact]
    public async Task English_promotes_while_the_German_rendering_stays_in_quarantine()
    {
        using var h = new IngestTestHarness();

        const string en = "Well met, <PLAYERFIRSTNAME>.";
        const string de = "Sei gegruesst, <PLAYERFIRSTNAME>.";

        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line(en));
        await h.SubmitLive(IngestTestHarness.InstallB, IngestTestHarness.Line(en));
        await h.SubmitLive(IngestTestHarness.InstallC, IngestTestHarness.Line(en));
        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line(de, lang: "de"));

        await using var db = h.NewContext();
        var line = await db.Lines.SingleAsync();
        Assert.Equal("en", line.Lang);

        var german = await db.PendingLines.SingleAsync(p => p.Lang == "de");
        Assert.Equal(PendingState.Pending, german.State);
    }

    [Fact]
    public async Task Identical_text_in_two_languages_stays_two_separate_entries()
    {
        using var h = new IngestTestHarness();

        // Proper names are often spelled the same in every language, so the same text really does
        // arrive under two language tags. `lang` is part of the key precisely so they do not merge.
        const string text = "Alphinaud";
        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line(text));
        await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line(text, lang: "fr"));

        await using var db = h.NewContext();
        Assert.Equal(2, await db.PendingLines.CountAsync());
    }

    [Fact]
    public async Task Unknown_language_is_dropped_rather_than_stored()
    {
        using var h = new IngestTestHarness();

        var result = await h.SubmitLive(IngestTestHarness.InstallA, IngestTestHarness.Line(lang: "kr"));

        Assert.Equal(1, result.DroppedInvalid);
        await using var db = h.NewContext();
        Assert.Empty(db.PendingLines);
    }
}
