using Echolines.Server.Data;
using Echolines.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Server.Services;

/// <summary>One page of results plus the total, so the UI can draw a pager without counting rows itself.</summary>
public record Page<T>(IReadOnlyList<T> Items, int Total, int PageIndex, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

public record PendingFilter
{
    public PendingState State { get; init; } = PendingState.Pending;
    public string? Lang { get; init; }
    public string? Npc { get; init; }
    public string? Text { get; init; }
    public int? MinVotes { get; init; }
    public int PageIndex { get; init; }
    public int PageSize { get; init; } = 50;
    public PendingSort Sort { get; init; } = PendingSort.NewestFirst;
}

public enum PendingSort
{
    NewestFirst,
    OldestFirst,
    MostVotes,
}

/// <summary>Which side of the origin comparison the operator wants to look at (the point of Phase 6).</summary>
public enum LineView
{
    All,

    /// <summary>Seen live, never harvested — the harvest gap. This is what the whole build is for.</summary>
    HarvestGap,

    /// <summary>Harvested, never seen live — dead content, unused lines, a patch's advance data.</summary>
    NeverEncountered,

    /// <summary>From the operator's Drive import: nothing at all is known about it.</summary>
    OperatorOnly,
}

public record LineFilter
{
    public LineView View { get; init; } = LineView.All;
    public string? Lang { get; init; }
    public string? Npc { get; init; }
    public string? Text { get; init; }
    public int PageIndex { get; init; }
    public int PageSize { get; init; } = 50;
}

public record InsightCounts(
    int Lines,
    int HarvestGap,
    int NeverEncountered,
    int OperatorOnly,
    int Pending,
    int Rejected,
    int Promoted);

public interface ILineReviewService
{
    Task<Page<PendingLine>> QueryPendingAsync(PendingFilter filter, CancellationToken ct = default);

    Task<Page<Line>> QueryLinesAsync(LineFilter filter, CancellationToken ct = default);

    Task<InsightCounts> GetCountsAsync(CancellationToken ct = default);

    /// <summary>Releases entries by hand, below the consensus threshold. Returns how many actually moved.</summary>
    Task<int> ApproveAsync(IReadOnlyCollection<int> pendingIds, CancellationToken ct = default);

    /// <summary>
    /// Releases every pending entry the filter matches, not just one page of it. Works in batches so a
    /// six-figure quarantine never sits in memory at once; <paramref name="progress"/> receives the running
    /// total after each batch. Paging and sort of the filter are ignored. Returns how many actually moved.
    /// </summary>
    Task<int> ApproveAllAsync(PendingFilter filter, Action<int>? progress = null, CancellationToken ct = default);

    /// <summary>Turns entries down. They stay in the table but leave the default view for good.</summary>
    Task<int> RejectAsync(IReadOnlyCollection<int> pendingIds, CancellationToken ct = default);
}

public class LineReviewService(EcholinesDbContext db, TimeProvider clock) : ILineReviewService
{
    /// <summary>Entries per round trip of <see cref="ApproveAllAsync"/>. Large enough to be quick, small enough to stay flat in memory.</summary>
    public const int ApproveAllBatchSize = 1000;

    /// <summary>The filter conditions, shared by the page view and the release-all path so both always mean the same set.</summary>
    private static IQueryable<PendingLine> Matching(IQueryable<PendingLine> query, PendingFilter filter)
    {
        query = query.Where(p => p.State == filter.State);

        if (!string.IsNullOrWhiteSpace(filter.Lang))
            query = query.Where(p => p.Lang == filter.Lang);

        if (!string.IsNullOrWhiteSpace(filter.Npc))
            query = query.Where(p => EF.Functions.Like(p.NpcName, $"%{filter.Npc}%"));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(p => EF.Functions.Like(p.Text, $"%{filter.Text}%"));

        if (filter.MinVotes is > 0)
            query = query.Where(p => p.HitCount >= filter.MinVotes);

        return query;
    }

    public async Task<Page<PendingLine>> QueryPendingAsync(PendingFilter filter, CancellationToken ct = default)
    {
        var query = Matching(db.PendingLines.Include(p => p.Votes), filter);

        var total = await query.CountAsync(ct);

        query = filter.Sort switch
        {
            PendingSort.OldestFirst => query.OrderBy(p => p.FirstSeenUtc).ThenBy(p => p.Id),
            PendingSort.MostVotes => query.OrderByDescending(p => p.HitCount).ThenByDescending(p => p.Id),
            _ => query.OrderByDescending(p => p.LastSeenUtc).ThenByDescending(p => p.Id),
        };

        var items = await query
            .Skip(filter.PageIndex * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new Page<PendingLine>(items, total, filter.PageIndex, filter.PageSize);
    }

    public async Task<Page<Line>> QueryLinesAsync(LineFilter filter, CancellationToken ct = default)
    {
        var query = db.Lines.AsQueryable();

        query = filter.View switch
        {
            LineView.HarvestGap => query.Where(l => l.SeenLive && !l.SeenHarvest),
            LineView.NeverEncountered => query.Where(l => l.SeenHarvest && !l.SeenLive),
            LineView.OperatorOnly => query.Where(l => l.OriginOperator && !l.SeenLive && !l.SeenHarvest),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(filter.Lang))
            query = query.Where(l => l.Lang == filter.Lang);

        if (!string.IsNullOrWhiteSpace(filter.Npc))
            query = query.Where(l => EF.Functions.Like(l.NpcName, $"%{filter.Npc}%"));

        if (!string.IsNullOrWhiteSpace(filter.Text))
            query = query.Where(l => EF.Functions.Like(l.Text, $"%{filter.Text}%"));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.LastSeenUtc)
            .ThenByDescending(l => l.Id)
            .Skip(filter.PageIndex * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return new Page<Line>(items, total, filter.PageIndex, filter.PageSize);
    }

    public async Task<InsightCounts> GetCountsAsync(CancellationToken ct = default) => new(
        Lines: await db.Lines.CountAsync(ct),
        HarvestGap: await db.Lines.CountAsync(l => l.SeenLive && !l.SeenHarvest, ct),
        NeverEncountered: await db.Lines.CountAsync(l => l.SeenHarvest && !l.SeenLive, ct),
        OperatorOnly: await db.Lines.CountAsync(l => l.OriginOperator && !l.SeenLive && !l.SeenHarvest, ct),
        Pending: await db.PendingLines.CountAsync(p => p.State == PendingState.Pending, ct),
        Rejected: await db.PendingLines.CountAsync(p => p.State == PendingState.Rejected, ct),
        Promoted: await db.PendingLines.CountAsync(p => p.State == PendingState.Promoted, ct));

    public async Task<int> ApproveAsync(IReadOnlyCollection<int> pendingIds, CancellationToken ct = default)
    {
        if (pendingIds.Count == 0)
            return 0;

        var now = clock.GetUtcNow().UtcDateTime;

        var entries = await db.PendingLines
            .Include(p => p.Votes)
            .Where(p => pendingIds.Contains(p.Id) && p.State == PendingState.Pending)
            .ToListAsync(ct);

        return await PromoteAsync(entries, now, ct);
    }

    public async Task<int> ApproveAllAsync(PendingFilter filter, Action<int>? progress = null, CancellationToken ct = default)
    {
        // Only pending entries can be released, whatever state the page happens to be showing.
        filter = filter with { State = PendingState.Pending };
        var now = clock.GetUtcNow().UtcDateTime;
        var moved = 0;
        var afterId = 0;

        while (true)
        {
            // Keyset on the id rather than "take the first N still pending": an entry that does not move
            // for whatever reason would otherwise be picked up again on every round, forever.
            var cursor = afterId;
            var entries = await Matching(db.PendingLines.Include(p => p.Votes), filter)
                .Where(p => p.Id > cursor)
                .OrderBy(p => p.Id)
                .Take(ApproveAllBatchSize)
                .ToListAsync(ct);

            if (entries.Count == 0)
                break;

            afterId = entries[^1].Id;
            moved += await PromoteAsync(entries, now, ct);

            // A long-lived (circuit-scoped) context would otherwise keep every released row tracked.
            db.ChangeTracker.Clear();
            progress?.Invoke(moved);
        }

        return moved;
    }

    private async Task<int> PromoteAsync(List<PendingLine> entries, DateTime now, CancellationToken ct)
    {
        if (entries.Count == 0)
            return 0;

        // The Drive import writes straight into `lines`, so a pending entry can duplicate a row that is
        // already public. Merging beats inserting: the unique index would reject the insert anyway, and
        // the votes still carry origin information the existing row may be missing.
        var hashes = entries.Select(p => p.TextHash).Distinct().ToList();
        var existing = (await db.Lines.Where(l => hashes.Contains(l.TextHash)).ToListAsync(ct))
            .ToDictionary(l => (l.Lang, l.NpcName, l.TextHash));

        var moved = 0;

        foreach (var pending in entries)
        {
            if (existing.TryGetValue((pending.Lang, pending.NpcName, pending.TextHash), out var line))
                LinePromotion.MergeInto(line, pending, now);
            else
                db.Lines.Add(LinePromotion.CreateFrom(pending, now));

            pending.State = PendingState.Promoted;
            moved++;
        }

        await db.SaveChangesAsync(ct);
        return moved;
    }

    public async Task<int> RejectAsync(IReadOnlyCollection<int> pendingIds, CancellationToken ct = default)
    {
        if (pendingIds.Count == 0)
            return 0;

        // Rejected, not deleted. A deleted entry is re-created by the next client that reports the line
        // and lands straight back in the work list — the state is the only thing that makes a decision stick.
        var affected = await db.PendingLines
            .Where(p => pendingIds.Contains(p.Id) && p.State == PendingState.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.State, PendingState.Rejected), ct);

        return affected;
    }
}
