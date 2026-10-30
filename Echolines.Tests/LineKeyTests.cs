using Echolines.Server.Data;
using Echolines.Server.Services;

namespace Echolines.Tests;

/// <summary>
/// The key has to match Echokraut's <c>TalkTextHelper.VoiceMessageToFileName</c> exactly — it is the
/// same string as the WAV file name and the <c>*.ekpack</c> entry. A drift here does not throw; it
/// quietly splits one line into two and nobody notices for months.
/// </summary>
public class LineKeyTests
{
    [Theory]
    [InlineData("Well met, adventurer!", "wellmetadventurer")]
    [InlineData("Hmm... interesting.", "hmminteresting")]
    [InlineData("A-ha! There you are.", "ahathereyouare")]
    [InlineData("MiXeD CaSe", "mixedcase")]
    public void Matches_the_plugin_slug(string text, string expected) =>
        Assert.Equal(expected, LineKey.FromText(text));

    [Fact]
    public void Strips_the_characters_Windows_forbids_in_a_file_name()
    {
        // ⚠ The regression guard for the platform trap: the plugin calls Path.GetInvalidFileNameChars(),
        // which drops all of these on Windows but only '/' and NUL on the Linux container this server
        // runs in. A colon is common in FFXIV dialogue, so getting this wrong would split real lines.
        Assert.Equal("thusspokebahamut", LineKey.FromText("Thus spoke: \"Bahamut\"?"));
        Assert.Equal("abc", LineKey.FromText("a<b>c"));
        Assert.Equal("ab", LineKey.FromText("a|b"));
        Assert.Equal("ab", LineKey.FromText("a\\b"));
        Assert.Equal("ab", LineKey.FromText("a/b"));
        Assert.Equal("ab", LineKey.FromText("a*b"));
    }

    [Fact]
    public void Truncates_at_the_plugin_limit()
    {
        var text = new string('a', 500);
        Assert.Equal(LineLimits.MaxHashLength, LineKey.FromText(text).Length);
    }

    [Fact]
    public void Folds_the_player_name_placeholder_into_the_key_the_same_way_for_everyone()
    {
        // The angle brackets are themselves invalid file-name characters, so the placeholder ends up as
        // bare text. What matters is that it is the *same* text for every player: one line stays one
        // entry instead of splitting into one per character who ever spoke to that NPC.
        Assert.Equal("wellmetplayerfirstname", LineKey.FromText("Well met, <PLAYERFIRSTNAME>"));
        Assert.Equal(
            LineKey.FromText("Well met, <PLAYERFIRSTNAME>"),
            LineKey.FromText("Well met, <playerfirstname>"));
    }
}
