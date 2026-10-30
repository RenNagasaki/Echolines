using Echolines.Server.Data.Entities;

namespace Echolines.Server.Services;

/// <summary>
/// The one place a quarantined line turns into a public one. Used both by the automatic X = 3 route
/// and by the operator's manual release in the admin UI — two copies of this would drift, and the
/// drift would show up as origin flags that quietly disagree with the votes they were built from.
/// </summary>
public static class LinePromotion
{
    public static Line CreateFrom(PendingLine pending, DateTime now) => new()
    {
        Lang = pending.Lang,
        NpcName = pending.NpcName,
        Gender = pending.Gender,
        Race = pending.Race,
        NpcBaseId = pending.NpcBaseId,
        Text = pending.Text,
        TextHash = pending.TextHash,
        FirstSeenUtc = pending.FirstSeenUtc,
        LastSeenUtc = now,
        PromotedUtc = now,
        SourceCount = pending.Votes.Count,
        SeenLive = pending.Votes.Any(v => v.Origin == LineOrigin.Live),
        SeenHarvest = pending.Votes.Any(v => v.Origin == LineOrigin.Harvest),
        OriginOperator = false,
    };

    /// <summary>
    /// Folds a pending entry's votes into a line that already exists — the case where the operator
    /// releases something the Drive import had already put in the main table.
    /// </summary>
    /// <returns>True if anything changed.</returns>
    public static bool MergeInto(Line line, PendingLine pending, DateTime now)
    {
        var changed = false;

        if (!line.SeenLive && pending.Votes.Any(v => v.Origin == LineOrigin.Live))
        {
            line.SeenLive = true;
            changed = true;
        }

        if (!line.SeenHarvest && pending.Votes.Any(v => v.Origin == LineOrigin.Harvest))
        {
            line.SeenHarvest = true;
            changed = true;
        }

        if (pending.Votes.Count > line.SourceCount)
        {
            line.SourceCount = pending.Votes.Count;
            changed = true;
        }

        if (changed)
            line.LastSeenUtc = now;

        return changed;
    }
}
