namespace Echolines.Server.Data.Entities;

/// <summary>Where a single vote came from. Stored per vote, aggregated onto <see cref="Line"/>.</summary>
public enum LineOrigin
{
    /// <summary>A player actually saw the line in a dialogue (POST /v1/lines).</summary>
    Live = 0,

    /// <summary>A plugin-side harvest read the line out of the game files (POST /v1/lines/import).</summary>
    Harvest = 1,
}

/// <summary>Lifecycle of a quarantined line. Nothing is ever deleted, so this is how entries leave the work list.</summary>
public enum PendingState
{
    /// <summary>Waiting for consensus or for a manual decision in the admin UI.</summary>
    Pending = 0,

    /// <summary>Turned down by the operator. Further votes are silently dropped so it cannot come back.</summary>
    Rejected = 1,

    /// <summary>Copied into <see cref="Line"/>. The row and its votes stay for the origin trail.</summary>
    Promoted = 2,
}
