using Echolines.Server.Data.Entities;
using Echolines.Server.Models;

namespace Echolines.Server.Services;

public interface ILineIngestService
{
    /// <summary>
    /// Takes one batch of reported lines from one installation. Never throws for bad content —
    /// unusable lines are counted and dropped, because both endpoints answer 200 in every case and
    /// the client must never show an error in game.
    /// </summary>
    Task<IngestResult> IngestAsync(
        SubmitRequest request,
        LineOrigin origin,
        int maxLines,
        CancellationToken ct = default);
}
