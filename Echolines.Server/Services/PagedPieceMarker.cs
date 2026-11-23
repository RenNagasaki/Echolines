using Echolines.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Server.Services;

/// <summary>
/// Recognises the window texts of a paged line: a line whose text is part of a <see cref="Data.Entities.Line.Paged"/>
/// line of the same language and the same NPC is a <see cref="Data.Entities.Line.PagedPiece"/>.
/// </summary>
/// <remarks>
/// <para>
/// The live path reports what the Talk window shows, so a row the game spreads over several windows
/// arrives as several shorter lines no harvest will ever deliver under their own key. Without this they
/// all read as harvest gap.
/// </para>
/// <para>
/// Decided at write time, not in the views: a substring test per view and per count would compare
/// every gap line with every paged line of its NPC (the narrator alone holds most of the quest text),
/// on every page load. Here it runs only for lines that were just created or changed, and each test is
/// bounded by the <c>(lang, npc_name, paged)</c> index.
/// </para>
/// <para>
/// Both directions are needed because either side can arrive first: the window texts from players
/// before the harvest delivers the whole row, or the harvest's row before anybody met it.
/// </para>
/// </remarks>
public interface IPagedPieceMarker
{
    /// <summary>
    /// Re-examines the given lines after they were saved: as a possible piece of a paged line, and — if
    /// they are paged themselves — as the whole row other lines are pieces of. Returns how many lines
    /// were newly marked. Sticky like <c>Paged</c>: nothing is ever unmarked.
    /// </summary>
    Task<int> MarkAsync(IReadOnlyCollection<int> lineIds, CancellationToken ct = default);
}

public class PagedPieceMarker(EcholinesDbContext db) : IPagedPieceMarker
{
    public async Task<int> MarkAsync(IReadOnlyCollection<int> lineIds, CancellationToken ct = default)
    {
        if (lineIds.Count == 0)
            return 0;

        var ids = lineIds.Distinct().ToList();

        // Direction 1: a touched line is a piece of a paged line that already exists. Driven by the
        // primary key, the inner test seeks the (lang, npc_name, paged) index.
        var marked = await db.Lines
            .Where(l => ids.Contains(l.Id) && !l.Paged && !l.PagedPiece)
            .Where(l => db.Lines.Any(w =>
                w.Paged
                && w.Lang == l.Lang
                && w.NpcName == l.NpcName
                && w.Text.Replace("-", "").Replace(" ", "")
                    .Contains(l.Text.Replace("-", "").Replace(" ", ""))))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.PagedPiece, true), ct);

        // Direction 2: a touched line is (now) paged, so existing lines may be its pieces. One statement
        // per whole row with its text as a parameter keeps the scan inside that one NPC's lines.
        var rows = await db.Lines
            .AsNoTracking()
            .Where(w => ids.Contains(w.Id) && w.Paged)
            .Select(w => new { w.Id, w.Lang, w.NpcName, w.Text })
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            var whole = Comparable(row.Text);

            marked += await db.Lines
                .Where(l => l.Lang == row.Lang && l.NpcName == row.NpcName && l.Id != row.Id
                            && !l.Paged && !l.PagedPiece
                            && whole.Contains(l.Text.Replace("-", "").Replace(" ", "")))
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.PagedPiece, true), ct);
        }

        return marked;
    }

    /// <summary>
    /// The form both sides are compared in. A hyphen and a blank are dropped on both sides: a word the
    /// window wraps comes back from live play as "Vor-bereitungen" where the sheet row says
    /// "Vorbereitungen", and dropping them everywhere keeps genuine hyphens comparing equal.
    /// Must match the SQL expression used in <see cref="MarkAsync"/>.
    /// </summary>
    internal static string Comparable(string text) => text.Replace("-", "").Replace(" ", "");
}
