namespace Echolines.Server.Services;

/// <summary>
/// Normalises the speaker so the same line reported by different paths ends up as one entry.
/// </summary>
/// <remarks>
/// <para>
/// Narrator lines — quest text with no speaker, SYSTEM/QIB/POPMESSAGE entries — are a large share of
/// FFXIV dialogue, and Echokraut labels them two different ways depending on where they come from:
/// </para>
/// <list type="bullet">
/// <item>the live path passes an <b>empty</b> name (<c>AddonTalkHelper</c>: <c>state.Speaker ?? ""</c>),</item>
/// <item>the harvest labels them with a localised display name — <c>Narrator</c>, <c>Erzähler</c>,
/// <c>Narrateur</c>, <c>ナレーター</c> — under the internal key <c>SYSTEM</c>.</item>
/// </list>
/// <para>
/// Both are the same speaker. Stored as they arrive, one line would sit in the database twice and
/// never gather the three votes it needs, because half of them would land on the other spelling.
/// They are therefore all folded onto Echokraut's own internal key, <c>SYSTEM</c>.
/// </para>
/// </remarks>
public static class NpcName
{
    /// <summary>Echokraut's internal key for narrator dialogue (<c>NpcNameKey = "SYSTEM"</c>).</summary>
    public const string Narrator = "SYSTEM";

    /// <summary>The display names the harvest produces for its SYSTEM entries, one per game language.</summary>
    private static readonly HashSet<string> NarratorAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        Narrator,
        "Narrator",
        "Erzähler",
        "Erzaehler",
        "Narrateur",
        "ナレーター",
    };

    /// <summary>
    /// Returns the speaker to store. An absent speaker is the narrator, not a broken report — refusing
    /// it would throw away most of the quest dialogue in the game.
    /// </summary>
    public static string Canonicalise(string? npc)
    {
        var trimmed = npc?.Trim();

        return string.IsNullOrEmpty(trimmed) || NarratorAliases.Contains(trimmed)
            ? Narrator
            : trimmed;
    }

    public static bool IsNarrator(string npc) => npc == Narrator;
}
