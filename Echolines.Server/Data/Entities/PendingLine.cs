namespace Echolines.Server.Data.Entities;

/// <summary>
/// Quarantine. Everything the server does not already know lands here first and is only copied into
/// <see cref="Line"/> once <c>Ingest:PromotionThreshold</c> distinct installations have reported it
/// (or the operator promotes it by hand).
/// </summary>
/// <remarks>
/// This table is never pruned (decided 2026-08-10): entries that never reach the threshold are exactly
/// the interesting material — the lines the harvest does not know. It therefore grows without bound and
/// is, in the long run, larger than <see cref="Line"/>. Pagination and the indexes declared in
/// <see cref="EcholinesDbContext"/> are load-bearing, not decoration.
/// </remarks>
public class PendingLine
{
    public int Id { get; set; }

    public required string Lang { get; set; }
    public required string NpcName { get; set; }
    public string? Gender { get; set; }
    public string? Race { get; set; }
    public long? NpcBaseId { get; set; }
    public required string Text { get; set; }

    /// <summary>Server-computed slug of <see cref="Text"/>, see <see cref="Services.LineKey"/>.</summary>
    public required string TextHash { get; set; }

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Number of accepted votes. Only counts up when a genuinely new voter is recorded.</summary>
    public int HitCount { get; set; }

    public PendingState State { get; set; } = PendingState.Pending;

    public List<PendingVote> Votes { get; set; } = [];
}
