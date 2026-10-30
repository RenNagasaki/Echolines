using Echolines.Server.Data.Entities;
using Echolines.Server.Models;
using Echolines.Server.Services;
using Microsoft.Extensions.Options;

namespace Echolines.Server.Endpoints;

public static class LineEndpoints
{
    public static void MapLineEndpoints(this IEndpointRouteBuilder app)
    {
        // Versioned path so a format change does not break clients that are already in the wild —
        // a Dalamud plugin update reaches users over weeks, not minutes.
        var v1 = app.MapGroup("/v1");

        // The live path: a player saw a dialogue line. Batched by the client (~60 s or 50 lines).
        v1.MapPost("/lines", async (
                SubmitRequest request,
                ILineIngestService ingest,
                IOptions<IngestOptions> options,
                CancellationToken ct) =>
            {
                if (TooLarge(request, options.Value.MaxLinesPerLiveRequest, out var tooLarge))
                    return tooLarge;

                await ingest.IngestAsync(request, LineOrigin.Live, options.Value.MaxLinesPerLiveRequest, ct);
                return Results.Ok(EmptyBody);
            })
            .RequireRateLimiting(RateLimitPolicies.Live);

        // The bulk path: a plugin-side harvest submits one language's entire result, ~288 000 lines in
        // chunks of 1000. Anonymous like the live path, but on its own budget so a harvest running for
        // hours does not eat the live path's allowance — and, without exception, into quarantine:
        // 288 000 lines from one sender still cannot push anything public past X = 3.
        v1.MapPost("/lines/import", async (
                SubmitRequest request,
                ILineIngestService ingest,
                IOptions<IngestOptions> options,
                CancellationToken ct) =>
            {
                if (TooLarge(request, options.Value.MaxLinesPerImportRequest, out var tooLarge))
                    return tooLarge;

                await ingest.IngestAsync(request, LineOrigin.Harvest, options.Value.MaxLinesPerImportRequest, ct);
                return Results.Ok(EmptyBody);
            })
            .RequireRateLimiting(RateLimitPolicies.Import);
    }

    /// <summary>An empty object rather than an empty body, so a client's JSON parser has something to chew on.</summary>
    private static readonly Dictionary<string, string> EmptyBody = [];

    /// <summary>
    /// 400 is reserved for a payload that is broken or oversized. Everything else answers 200, including
    /// "all of this was already known" — the client has no UI and must never learn to treat a report as
    /// a question with an interesting answer.
    /// </summary>
    private static bool TooLarge(SubmitRequest request, int maxLines, out IResult result)
    {
        if (request.Lines is { Count: > 0 } lines && lines.Count > maxLines)
        {
            result = Results.BadRequest(new { error = "too_many_lines", maxLines });
            return true;
        }

        result = Results.Empty;
        return false;
    }
}
