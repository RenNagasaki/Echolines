using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Echolines.Server.Services;

/// <summary>
/// Rate limiting for an endpoint that is, by design, unauthenticated and writable. Without any limit
/// it is simply an open barn door; an API key in the plugin would not help, because it sits in the DLL
/// and is extracted in minutes.
/// </summary>
/// <remarks>
/// Partitioned by installation id <b>and</b> by IP. The installation id is what an honest client sends
/// and what makes the limit fair per user; the IP is what stops someone from minting a fresh GUID for
/// every request. The IP is used for this decision only — it is held in memory by the limiter and is
/// never written next to a line.
/// </remarks>
public static class RateLimitPolicies
{
    public const string Live = "live";
    public const string Import = "import";

    /// <param name="extra">Policies owned by another area (the admin login) that still belong in the
    /// one limiter the app has.</param>
    public static void AddEcholinesRateLimiting(
        this IServiceCollection services,
        RateLimitOptionsConfig config,
        Action<RateLimiterOptions>? extra = null)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // The client ignores a 429 and simply tries again later; it must never surface in game.
            o.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(new { error = "rate_limited" }), ct);
            };

            o.AddPolicy(Live, ctx => Partition(ctx, "live", config.LivePermitPerMinute));
            o.AddPolicy(Import, ctx => Partition(ctx, "import", config.ImportPermitPerMinute));

            extra?.Invoke(o);
        });
    }

    private static RateLimitPartition<string> Partition(HttpContext ctx, string scope, int permitPerMinute)
    {
        var installId = ctx.Request.Headers["X-Install-Id"].ToString();
        if (string.IsNullOrWhiteSpace(installId))
            installId = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{scope}:{installId}:{ip}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    }
}

public class RateLimitOptionsConfig
{
    public const string SectionName = "RateLimit";

    /// <summary>Live reports arrive roughly once a minute per client; the headroom absorbs retries.</summary>
    public int LivePermitPerMinute { get; set; } = 20;

    /// <summary>
    /// Generous but capped: a harvest is ~288 chunks and may take hours, and that is the intent —
    /// it must not be able to run through in minutes.
    /// </summary>
    public int ImportPermitPerMinute { get; set; } = 6;
}
