namespace Echolines.Server.Data.Entities;

/// <summary>
/// One installation's vote for one quarantined line. <c>UNIQUE(PendingId, VoterKey)</c> is the entire
/// consensus mechanism: it is what makes "X reports" mean "X different installations" rather than
/// "one sender pressing send X times".
/// </summary>
public class PendingVote
{
    public int Id { get; set; }

    public int PendingId { get; set; }
    public PendingLine? Pending { get; set; }

    /// <summary>The installation's random GUID. Derived from nothing, tied to no person (variant A).</summary>
    public required string VoterKey { get; set; }

    public LineOrigin Origin { get; set; }

    public DateTime SeenUtc { get; set; }
}
