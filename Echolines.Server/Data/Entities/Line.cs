namespace Echolines.Server.Data.Entities;

/// <summary>
/// The public stock. A line is here because three separate installations reported it, because the
/// operator promoted it by hand, or because it came from the operator's own Google Drive import.
/// </summary>
/// <remarks>
/// This table is also the server's only notion of "already known". The server holds no harvest stock of
/// its own and has no correlation to a game version — whatever arrives, arrives. That is why
/// <see cref="SeenLive"/>/<see cref="SeenHarvest"/> exist: they answer "what does the harvest not know?"
/// from inside the database, and cannot go stale with a game patch the way a frozen reference stock would.
/// </remarks>
public class Line
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
    public DateTime? PromotedUtc { get; set; }

    /// <summary>Distinct installations that voted for this line at promotion time. 0 for operator imports.</summary>
    public int SourceCount { get; set; }

    /// <summary>A player has seen this line in a dialogue.</summary>
    public bool SeenLive { get; set; }

    /// <summary>A harvest has read this line out of the game files.</summary>
    public bool SeenHarvest { get; set; }

    /// <summary>
    /// Came from the operator's Google Drive bulk import — neither live nor harvest. Without this third
    /// origin those rows would show up in the "exists in the game files but nobody has met it" view,
    /// when in truth nothing at all is known about them.
    /// </summary>
    public bool OriginOperator { get; set; }

    /// <summary>
    /// The game shows this line spread over several Talk windows (long <c>quest/*</c> and
    /// <c>cut_scene/*</c> rows). Sticky: once any report carries it, it stays set — a later report
    /// without it is a client that cannot tell, not evidence that the line fits one window.
    /// </summary>
    /// <remarks>
    /// Why it matters: the harvest reports such a row whole, the live path only the window texts, so
    /// the whole row never collects a live vote under its own key. Consumers need to know that a
    /// row's absence from live play is expected, and that its pieces arrive as separate lines.
    /// </remarks>
    public bool Paged { get; set; }

    /// <summary>
    /// This line is one Talk window of a <see cref="Paged"/> line of the same language and NPC: its
    /// text is part of that row's text. Set by <see cref="Services.PagedPieceMarker"/>, sticky.
    /// </summary>
    /// <remarks>
    /// Such a line is seen live but no harvest delivers it under its own key, because the harvest
    /// reports the whole row. It is therefore explained, not a harvest gap — the Insights views keep
    /// it out of <c>HarvestGap</c> and show it on its own.
    /// </remarks>
    public bool PagedPiece { get; set; }
}
