using System.Threading.RateLimiting;
using Echolines.Server.Admin;
using Echolines.Server.Components;
using Echolines.Server.Data;
using Echolines.Server.Endpoints;
using Echolines.Server.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

// Generating the operator's password hash is a one-shot job, not a reason to install a second tool
// or to paste a password into an online hasher:
//   docker compose run --rm server dotnet Echolines.Server.dll hash-password
if (args is ["hash-password", ..])
{
    Console.Write("Password: ");
    var password = Console.ReadLine() ?? "";
    Console.WriteLine(AdminPasswordHasher.Hash(password));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// "none" lets a host register its own provider instead — the test project swaps in SQLite, because
// two providers in one service collection is an error EF refuses to guess its way out of.
if (!string.Equals(builder.Configuration["Database:Provider"], "none", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<EcholinesDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
}

builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection(IngestOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

var adminOptions = new AdminOptions();
builder.Configuration.GetSection(AdminOptions.SectionName).Bind(adminOptions);

var rateLimits = new RateLimitOptionsConfig();
builder.Configuration.GetSection(RateLimitOptionsConfig.SectionName).Bind(rateLimits);

builder.Services.AddEcholinesRateLimiting(rateLimits, o =>
    // Brute force is the only attack a single-account login has. Per IP, because there is no identity
    // to partition by before the password is checked.
    o.AddPolicy(AdminEndpoints.LoginRateLimitPolicy, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = adminOptions.LoginAttemptsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ILineIngestService, LineIngestService>();
builder.Services.AddScoped<ILineReviewService, LineReviewService>();
builder.Services.AddScoped<IDriveImportService, DriveImportService>();

builder.Services.AddAdminAuth();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

// Auto-migrate on startup, like the sibling Echocrowd.Server.
if (!app.Configuration.GetValue("Database:SkipMigrationOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EcholinesDbContext>();
    await db.Database.MigrateAsync();
}

// Behind a TLS-terminating reverse proxy the request reaches this container as plain HTTP from the
// proxy's own address. Two things break silently without this: every client shares one rate-limit
// bucket, because RemoteIpAddress is the proxy for all of them; and the admin cookie never gets its
// Secure flag, because CookieSecurePolicy.SameAsRequest sees http.
//
// Off by default and opt-in, because trusting X-Forwarded-For is only safe when something in front is
// actually rewriting it. On a directly exposed port it would let any caller claim any address and walk
// straight around the rate limit.
if (app.Configuration.GetValue("Server:BehindReverseProxy", false))
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = app.Configuration.GetValue("Server:ForwardLimit", 1),
    };

    // The proxy sits on the container network with an address we cannot know in advance, and the
    // default known-proxy list (loopback only) would drop its headers unread.
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();

    app.UseForwardedHeaders(forwarded);
}

app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapLineEndpoints();
app.MapAdminEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Plain liveness probe for the container orchestrator. Says nothing about the data.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

// No `public partial class Program` marker below: as of .NET 10 the generated entry-point class is
// already public, so WebApplicationFactory<Program> in the test project finds it on its own.
await app.RunAsync();
