using System.Text.Json;
using Echolines.Server.Data;
using Echolines.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Server.Services;

public record DriveImportItem(string FileName, Stream Content);

public record DriveImportConflict(string FileName, string Reason);

public record DriveImportReport
{
    public int Imported { get; init; }
    public int AlreadyKnown { get; init; }
    public int Failed => Conflicts.Count;
    public IReadOnlyList<DriveImportConflict> Conflicts { get; init; } = [];
}

public interface IDriveImportService
{
    Task<DriveImportReport> ImportAsync(IReadOnlyList<DriveImportItem> files, CancellationToken ct = default);
}

/// <summary>
/// Reads the old "one JSON file per line in Google Drive" stock into the main table. The material is
/// the operator's own, so it skips quarantine — but it is also the one source that can be silently
/// wrong, hence the conflict report.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ Two traps in the legacy format, both load-bearing here:
/// </para>
/// <para>
/// 1. <b>The JSON is not trustworthy for the enums.</b> <c>Gender</c>, <c>Race</c> and <c>Language</c>
/// were serialised by <c>System.Text.Json</c> without a string converter, so they are <i>numbers</i>.
/// Reorder an enum in the plugin and every old number points at a different value. The file <i>name</i>
/// carries the same three as plain text (<c>Gender.ToString()</c> and friends), so the name is the
/// authority and only <c>Text</c> is taken from the body.
/// </para>
/// <para>
/// 2. <b>The body may still contain a real character name.</b> <c>VoiceLine.GetFileName</c> tokenises
/// the player name for the file-name slug only — the serialised <c>Text</c> is the raw string. Comparing
/// the recomputed slug against the one in the file name catches exactly that case, and it goes into the
/// conflict report rather than into a public database.
/// </para>
/// </remarks>
public class DriveImportService(EcholinesDbContext db, TimeProvider clock, ILogger<DriveImportService> log)
    : IDriveImportService
{
    /// <summary>Dalamud's <c>ClientLanguage</c> names, as they appear in the file name.</summary>
    private static readonly Dictionary<string, string> LanguageNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Japanese"] = "ja",
        ["English"] = "en",
        ["German"] = "de",
        ["French"] = "fr",
    };

    public async Task<DriveImportReport> ImportAsync(
        IReadOnlyList<DriveImportItem> files,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var conflicts = new List<DriveImportConflict>();
        var parsed = new List<ParsedFile>();

        foreach (var file in files)
        {
            var (item, reason) = await ParseAsync(file, ct);
            if (item is null)
                conflicts.Add(new DriveImportConflict(file.FileName, reason!));
            else
                parsed.Add(item);
        }

        var imported = 0;
        var alreadyKnown = 0;

        if (parsed.Count > 0)
        {
            var hashes = parsed.Select(p => p.TextHash).Distinct().ToList();
            var existing = (await db.Lines.Where(l => hashes.Contains(l.TextHash)).ToListAsync(ct))
                .Select(l => (l.Lang, l.NpcName, l.TextHash))
                .ToHashSet();

            foreach (var p in parsed)
            {
                if (!existing.Add((p.Lang, p.Npc, p.TextHash)))
                {
                    alreadyKnown++;
                    continue;
                }

                db.Lines.Add(new Line
                {
                    Lang = p.Lang,
                    NpcName = p.Npc,
                    Gender = p.Gender,
                    Race = p.Race,
                    Text = p.Text,
                    TextHash = p.TextHash,
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    PromotedUtc = now,
                    SourceCount = 0,
                    SeenLive = false,
                    SeenHarvest = false,
                    // The third origin. Without it these rows would read as "exists in the game files but
                    // nobody has met it", when in truth nothing whatsoever is known about them.
                    OriginOperator = true,
                });

                imported++;
            }

            await db.SaveChangesAsync(ct);
        }

        if (log.IsEnabled(LogLevel.Information))
        {
            log.LogInformation(
                "Drive import: {Imported} imported, {Known} already known, {Failed} conflicts",
                imported, alreadyKnown, conflicts.Count);
        }

        return new DriveImportReport
        {
            Imported = imported,
            AlreadyKnown = alreadyKnown,
            Conflicts = conflicts,
        };
    }

    private static async Task<(ParsedFile? Item, string? Reason)> ParseAsync(
        DriveImportItem file,
        CancellationToken ct)
    {
        var name = Path.GetFileName(file.FileName);
        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return (null, "not a .json file");

        // {Language}_{Gender}_{Race}_{Name}_{slug}.json
        var stem = name[..^5];
        var parts = stem.Split('_');
        if (parts.Length < 5)
            return (null, "file name does not have the Language_Gender_Race_Name_slug shape");

        if (!LanguageNames.TryGetValue(parts[0], out var lang))
            return (null, $"unknown language '{parts[0]}' in the file name");

        var gender = parts[1];
        var race = parts[2];
        var rest = string.Join('_', parts[3..]);

        string? text;
        try
        {
            using var doc = await JsonDocument.ParseAsync(file.Content, cancellationToken: ct);
            text = doc.RootElement.TryGetProperty("Text", out var t) ? t.GetString() : null;
        }
        catch (JsonException ex)
        {
            return (null, $"unreadable JSON: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(text))
            return (null, "no Text in the JSON body");

        if (text.Length > LineLimits.MaxTextLength)
            return (null, "text exceeds the maximum length");

        var recomputed = LineKey.FromText(text);

        // The NPC name and the slug are BOTH in `rest`, separated by an underscore — but which one is
        // not decidable from the underscores alone. The plugin strips '_' when building a slug, yet the
        // stored names contain them anyway, because characters the plugin kept were substituted on the
        // way into Drive: "Oh, you are Lyse's friend" is stored as ...lyse_sfriend... So the split is
        // located by the recomputed slug instead of guessed: take the leftmost cut whose tail matches.
        var npc = SplitOffSlug(rest, recomputed);

        // No cut works ⇒ the file name and the body genuinely describe different lines, or the body
        // still holds the character name the file name had tokenised away. Neither is something to
        // guess at, and the second must never reach a public table.
        if (npc is null)
            return (null, $"the text does not reproduce the slug in the file name (recomputed '{recomputed}')");

        // An empty name segment is not a broken file: the live path writes "" for narrator lines, so
        // `English_None_Unknown__slug.json` is quest text with no speaker. See NpcName.
        return (new ParsedFile(lang, NpcName.Canonicalise(npc), Empty(gender), Empty(race), text, recomputed), null);
    }

    /// <summary>
    /// Cuts <paramref name="rest"/> ("{NpcName}_{slug}") at the underscore that makes the tail the
    /// expected slug, and returns the NPC name. Null when no cut does.
    /// </summary>
    /// <remarks>
    /// Leftmost first, so the longest possible tail wins: an NPC name may contain an underscore of its
    /// own ("Wandering_Minstrel"), and the tail has to be verified either way, so the first cut that
    /// verifies is the right one.
    /// </remarks>
    private static string? SplitOffSlug(string rest, string expectedSlug)
    {
        var expected = LineKey.Normalise(expectedSlug);

        for (var i = rest.IndexOf('_'); i >= 0; i = rest.IndexOf('_', i + 1))
        {
            if (LineKey.Normalise(rest[(i + 1)..]) == expected)
                return rest[..i];
        }

        return null;
    }

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record ParsedFile(
        string Lang,
        string Npc,
        string? Gender,
        string? Race,
        string Text,
        string TextHash);
}
