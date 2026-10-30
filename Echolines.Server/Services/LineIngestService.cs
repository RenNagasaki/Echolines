using Echolines.Server.Data;
using Echolines.Server.Data.Entities;
using Echolines.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Echolines.Server.Services;

/// <summary>
/// Consensus and promotion. This is the part of the system nobody checks by hand: it fails quietly,
/// the admin UI still looks plausible, and the mistake surfaces months later. Hence the test suite in
/// <c>Echolines.Tests</c> covers each rule below individually.
/// </summary>
public class LineIngestService(
    EcholinesDbContext db,
    IOptions<IngestOptions> options,
    TimeProvider clock,
    ILogger<LineIngestService> log) : ILineIngestService
{
    private readonly IngestOptions _options = options.Value;

    public async Task<IngestResult> IngestAsync(
        SubmitRequest request,
        LineOrigin origin,
        int maxLines,
        CancellationToken ct = default)
    {
        var voterKey = NormaliseInstallId(request.InstallId);
        if (voterKey is null)
            return new IngestResult { DroppedInvalid = request.Lines?.Count ?? 0 };

        var candidates = new List<Candidate>();
        var invalid = 0;
        var mismatched = 0;

        foreach (var line in (request.Lines ?? []).Take(maxLines))
        {
            var candidate = Validate(line, ref mismatched);
            if (candidate is null)
                invalid++;
            else
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            return new IngestResult { DroppedInvalid = invalid, HashMismatch = mismatched };

        // A single retry covers the one race this design has: two installations reporting the same
        // unknown line in the same instant, where both see "no such row" and both try to insert it.
        // The unique indexes turn that into a DbUpdateException; on the second pass one of them simply
        // finds the other's row and adds a vote to it.
        try
        {
            return await ProcessAsync(candidates, voterKey, origin, invalid, mismatched, ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogInformation(ex, "Concurrent insert for the same line key, retrying once");
            db.ChangeTracker.Clear();
            return await ProcessAsync(candidates, voterKey, origin, invalid, mismatched, ct);
        }
    }

    private async Task<IngestResult> ProcessAsync(
        List<Candidate> candidates,
        string voterKey,
        LineOrigin origin,
        int invalid,
        int mismatched,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        // Two queries per batch, not two per line: an import chunk carries 1000 lines and a
        // per-line round trip would make a 288 000-line harvest untenable.
        var hashes = candidates.Select(c => c.TextHash).Distinct().ToList();

        var knownLines = await db.Lines
            .Where(l => hashes.Contains(l.TextHash))
            .ToDictionaryAsync(l => new LineKeyTuple(l.Lang, l.NpcName, l.TextHash), ct);

        var knownPending = await db.PendingLines
            .Include(p => p.Votes)
            .Where(p => hashes.Contains(p.TextHash))
            .ToDictionaryAsync(p => new LineKeyTuple(p.Lang, p.NpcName, p.TextHash), ct);

        var accepted = 0;
        var alreadyKnown = 0;
        var newPending = 0;
        var newVotes = 0;
        var promoted = 0;
        var originUpdated = 0;
        var droppedRejected = 0;

        foreach (var c in candidates)
        {
            accepted++;
            var key = new LineKeyTuple(c.Lang, c.Npc, c.TextHash);

            if (knownLines.TryGetValue(key, out var existing))
            {
                alreadyKnown++;
                if (UpdateOrigin(existing, origin, now))
                    originUpdated++;
                continue;
            }

            if (!knownPending.TryGetValue(key, out var pending))
            {
                pending = NewPendingLine(c, now);
                db.PendingLines.Add(pending);
                knownPending[key] = pending;
                newPending++;
            }
            else if (pending.State == PendingState.Rejected)
            {
                // Swallow it. Without this a rejected entry collects votes again through ordinary
                // reports and walks straight back into the public stock the operator just kept it out of.
                droppedRejected++;
                continue;
            }

            if (pending.Votes.Any(v => v.VoterKey == voterKey))
                continue; // same installation, already counted — a repeated harvest costs nothing

            pending.Votes.Add(new PendingVote
            {
                VoterKey = voterKey,
                Origin = origin,
                SeenUtc = now,
                Pending = pending,
            });
            pending.HitCount++;
            pending.LastSeenUtc = now;
            newVotes++;

            if (pending.State == PendingState.Pending && pending.Votes.Count >= _options.PromotionThreshold)
            {
                var line = Promote(pending, now);
                knownLines[key] = line;
                promoted++;
            }
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);

        return new IngestResult
        {
            Accepted = accepted,
            AlreadyKnown = alreadyKnown,
            NewPending = newPending,
            NewVotes = newVotes,
            Promoted = promoted,
            OriginUpdated = originUpdated,
            DroppedRejected = droppedRejected,
            DroppedInvalid = invalid,
            HashMismatch = mismatched,
        };
    }

    /// <summary>
    /// "Already known ⇒ do nothing" is wrong and was the bug that once tipped the design over: a line
    /// promoted by three harvests would keep <c>SeenLive = false</c> forever, even after players had
    /// met it, and Phase 6 would report a harvest gap that closed long ago. The origin flags are
    /// therefore caught up here, on a line that already exists.
    /// </summary>
    /// <returns>True if anything was actually written.</returns>
    private bool UpdateOrigin(Line line, LineOrigin origin, DateTime now)
    {
        var changed = false;

        if (origin == LineOrigin.Live && !line.SeenLive)
        {
            line.SeenLive = true;
            changed = true;
        }
        else if (origin == LineOrigin.Harvest && !line.SeenHarvest)
        {
            line.SeenHarvest = true;
            changed = true;
        }

        // Coarse on purpose — see IngestOptions.LastSeenResolution.
        if (changed || now - line.LastSeenUtc >= _options.LastSeenResolution)
        {
            line.LastSeenUtc = now;
            changed = true;
        }

        return changed;
    }

    private Line Promote(PendingLine pending, DateTime now)
    {
        var line = LinePromotion.CreateFrom(pending, now);
        db.Lines.Add(line);

        // Marked, not deleted: the votes carry the origin trail, and dropping it exactly for the lines
        // that made it would leave "nothing is ever discarded" true only for the also-rans.
        pending.State = PendingState.Promoted;

        return line;
    }

    private static PendingLine NewPendingLine(Candidate c, DateTime now) => new()
    {
        Lang = c.Lang,
        NpcName = c.Npc,
        Gender = c.Gender,
        Race = c.Race,
        NpcBaseId = c.NpcBaseId,
        Text = c.Text,
        TextHash = c.TextHash,
        FirstSeenUtc = now,
        LastSeenUtc = now,
        HitCount = 0,
        State = PendingState.Pending,
    };

    private static Candidate? Validate(SubmitLine line, ref int mismatched)
    {
        if (!LineLimits.IsKnownLanguage(line.Lang))
            return null;

        // An absent speaker is the narrator, not a broken report — see NpcName. Rejecting it would
        // silently drop most of the quest dialogue in the game, since the live path sends "" for it.
        var npc = NpcName.Canonicalise(line.Npc);
        var text = line.Text?.Trim();

        if (npc.Length > LineLimits.MaxNpcNameLength)
            return null;

        if (string.IsNullOrEmpty(text) || text.Length > LineLimits.MaxTextLength)
            return null;

        // Recomputed, never taken from the client — see LineKey for why that matters.
        var hash = LineKey.FromText(text);
        if (hash.Length == 0)
            return null;

        if (!string.IsNullOrEmpty(line.Hash) && line.Hash != hash)
            mismatched++;

        return new Candidate(
            line.Lang!,
            npc,
            Trim(line.Gender, 32),
            Trim(line.Race, 32),
            line.NpcBaseId,
            text,
            hash);
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Length <= max ? value : value[..max];
    }

    /// <summary>Accepts only a well-formed GUID, so the vote key cannot be turned into a free-text field.</summary>
    private static string? NormaliseInstallId(string? installId) =>
        Guid.TryParse(installId, out var guid) && guid != Guid.Empty ? guid.ToString("D") : null;

    private sealed record Candidate(
        string Lang,
        string Npc,
        string? Gender,
        string? Race,
        long? NpcBaseId,
        string Text,
        string TextHash);

    private readonly record struct LineKeyTuple(string Lang, string Npc, string TextHash);
}
