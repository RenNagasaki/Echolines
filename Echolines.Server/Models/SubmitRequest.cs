using Echolines.Server.Data.Entities;

namespace Echolines.Server.Models;

/// <summary>Body of both <c>POST /v1/lines</c> and <c>POST /v1/lines/import</c>.</summary>
/// <remarks>
/// What is deliberately <b>absent</b> and must never be added: character name, world, content id,
/// play time, file paths, hardware identifiers. The client sends without the user doing anything,
/// so the payload is the privacy promise in executable form.
/// </remarks>
public class SubmitRequest
{
    /// <summary>Random GUID generated once per installation. Derived from nothing (variant A).</summary>
    public string? InstallId { get; set; }

    /// <summary>Only here so a format error can be attributed to a plugin build.</summary>
    public string? PluginVersion { get; set; }

    /// <summary><c>live</c> or <c>harvest</c>. Ignored on the endpoint that dictates its own origin.</summary>
    public string? Origin { get; set; }

    /// <summary>Chunk counters for the bulk import. Purely for the client's own resumability —
    /// the server may ignore them, because every line arrives idempotently anyway.</summary>
    public int? Chunk { get; set; }

    public int? ChunkCount { get; set; }

    public List<SubmitLine>? Lines { get; set; }
}

public class SubmitLine
{
    public string? Lang { get; set; }
    public string? Npc { get; set; }
    public string? Gender { get; set; }
    public string? Race { get; set; }
    public long? NpcBaseId { get; set; }

    /// <summary>Player-name-tokenised dialogue text. Never the real character name.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// The client's own slug. Accepted but <b>not</b> trusted — the server recomputes it from
    /// <see cref="Text"/> via <see cref="Services.LineKey"/> and uses its own value as the key.
    /// </summary>
    public string? Hash { get; set; }

    /// <summary>Which addon the line came from (<c>AddonTalk</c>, <c>AddonBattleTalk</c>, …). Informational.</summary>
    public string? Source { get; set; }
}

/// <summary>What one call to the ingest service did. Used for logging and tests, never returned to the client.</summary>
public record IngestResult
{
    public int Accepted { get; init; }
    public int AlreadyKnown { get; init; }
    public int NewPending { get; init; }
    public int NewVotes { get; init; }
    public int Promoted { get; init; }
    public int OriginUpdated { get; init; }
    public int DroppedRejected { get; init; }
    public int DroppedInvalid { get; init; }

    /// <summary>Client slug disagreed with the server's recomputation. Purely a health signal.</summary>
    public int HashMismatch { get; init; }

    /// <summary>True when nothing at all was written — the normal case once a user has played a while.</summary>
    public bool ReadOnly => NewPending == 0 && NewVotes == 0 && Promoted == 0 && OriginUpdated == 0;
}

public static class OriginParser
{
    public static LineOrigin Parse(string? origin) =>
        string.Equals(origin, "harvest", StringComparison.OrdinalIgnoreCase)
            ? LineOrigin.Harvest
            : LineOrigin.Live;
}
