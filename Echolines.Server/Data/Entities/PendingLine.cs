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

    /// <summary>
    /// The game shows this line spread over several Talk windows (long <c>quest/*</c> and
    /// <c>cut_scene/*</c> rows). Sticky: once any report carries it, it stays set, and promotion carries it into <see cref="Line"/> — a later report
    /// without it is a client that cannot tell, not evidence that the line fits one window.
    /// </summary>
    /// <remarks>
    /// Why it matters: the harvest reports such a row whole, the live path only the window texts, so
    /// the whole row never collects a live vote under its own key. Consumers need to know that a
    /// row's absence from live play is expected, and that its pieces arrive as separate lines.
    /// </remarks>
    public bool Paged { get; set; }

    public List<PendingVote> Votes { get; set; } = [];
}
