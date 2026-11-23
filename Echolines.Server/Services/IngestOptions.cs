namespace Echolines.Server.Services;

/// <summary>Bound from the <c>Ingest</c> configuration section.</summary>
public class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>
    /// Distinct installations required before a quarantined line becomes public. X = 3 (decided).
    /// Counted <b>per language</b> automatically, because the language is part of the line's key —
    /// three English reports do not promote the German rendering of the same line.
    /// </summary>
    public int PromotionThreshold { get; set; } = 3;

    /// <summary>Maximum lines in one <c>POST /v1/lines</c> body. The client batches at 50.</summary>
    public int MaxLinesPerLiveRequest { get; set; } = 200;

    /// <summary>Maximum lines in one <c>POST /v1/lines/import</c> chunk.</summary>
    public int MaxLinesPerImportRequest { get; set; } = 1000;

    /// <summary>
    /// How coarsely <c>last_seen_utc</c> on an already-known line is tracked. Bumping it on every
    /// report would turn the overwhelmingly common case — a line everybody already knows — into a
    /// write on every single dialogue in the game. A day's resolution keeps the field honest and the
    /// normal path read-only.
    /// </summary>
    public TimeSpan LastSeenResolution { get; set; } = TimeSpan.FromDays(1);
}
