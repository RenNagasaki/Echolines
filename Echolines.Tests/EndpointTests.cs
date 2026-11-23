using System.Net;
using System.Net.Http.Json;
using Echolines.Server.Data;
using Echolines.Server.Data.Entities;
using Echolines.Server.Models;
using Echolines.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Echolines.Tests;

/// <summary>Boots the real host with SQLite swapped in for Postgres, so routing, model binding and the
/// rate limiter are exercised as configured rather than as imagined.</summary>
public class EcholinesFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public EcholinesFactory()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "none");
        builder.UseSetting("Database:SkipMigrationOnStartup", "true");
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
            services.AddDbContext<EcholinesDbContext>(o => o.UseSqlite(_connection)));
    }

    public EcholinesDbContext NewContext() =>
        new(new DbContextOptionsBuilder<EcholinesDbContext>().UseSqlite(_connection).Options);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

public class EndpointTests
{
    private static SubmitRequest Body(string installId, int lineCount = 1, string origin = "live")
    {
        var lines = Enumerable.Range(0, lineCount)
            .Select(i => IngestTestHarness.Line($"Line number {i} of many."))
            .ToList();

        return new SubmitRequest { InstallId = installId, PluginVersion = "0.19.3.1", Origin = origin, Lines = lines };
    }

    [Fact]
    public async Task Both_endpoints_answer_200_even_when_everything_is_already_known()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/v1/lines", Body(IngestTestHarness.InstallA));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Same installation, same line, again — nothing to learn, still a 200. The client has no UI
        // and must never be given a reason to treat a report as a question.
        var second = await client.PostAsJsonAsync("/v1/lines", Body(IngestTestHarness.InstallA));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var import = await client.PostAsJsonAsync("/v1/lines/import", Body(IngestTestHarness.InstallA, origin: "harvest"));
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
    }

    [Fact]
    public async Task Oversized_batch_is_a_400_on_both_endpoints()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        var live = await client.PostAsJsonAsync("/v1/lines", Body(IngestTestHarness.InstallA, lineCount: 201));
        Assert.Equal(HttpStatusCode.BadRequest, live.StatusCode);

        var import = await client.PostAsJsonAsync("/v1/lines/import", Body(IngestTestHarness.InstallA, 1001, "harvest"));
        Assert.Equal(HttpStatusCode.BadRequest, import.StatusCode);

        // A live-sized batch is fine on the import endpoint; only the import limit applies there.
        var ok = await client.PostAsJsonAsync("/v1/lines/import", Body(IngestTestHarness.InstallA, 201, "harvest"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_answers_429_and_never_500()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Install-Id", IngestTestHarness.InstallB);

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 40; i++)
            statuses.Add((await client.PostAsJsonAsync("/v1/lines", Body(IngestTestHarness.InstallB))).StatusCode);

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
    }

    [Fact]
    public async Task Resent_import_chunk_is_idempotent_and_does_not_double_count()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        // The client aborted mid-harvest and started the chunk over — the plan says the server may
        // ignore chunk counters precisely because this has to be harmless.
        var chunk = Body(IngestTestHarness.InstallA, lineCount: 5, origin: "harvest");
        chunk.Chunk = 7;
        chunk.ChunkCount = 288;

        await client.PostAsJsonAsync("/v1/lines/import", chunk);
        await client.PostAsJsonAsync("/v1/lines/import", chunk);

        await using var db = factory.NewContext();
        Assert.Equal(5, await db.PendingLines.CountAsync());
        Assert.Equal(5, await db.PendingVotes.CountAsync());
        Assert.All(await db.PendingLines.ToListAsync(), p => Assert.Equal(1, p.HitCount));
    }

    [Fact]
    public async Task Import_never_bypasses_quarantine_however_much_one_sender_submits()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/v1/lines/import", Body(IngestTestHarness.InstallA, 200, "harvest"));

        await using var db = factory.NewContext();
        Assert.Empty(db.Lines);
        Assert.Equal(200, await db.PendingLines.CountAsync());
        Assert.All(await db.PendingLines.ToListAsync(), p => Assert.Equal(PendingState.Pending, p.State));
    }

    [Fact]
    public async Task Origin_is_decided_by_the_endpoint_not_by_the_payload()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        // A client claiming "harvest" on the live endpoint would otherwise get its report filed as
        // game-file evidence, which is exactly the distinction Phase 6 rests on.
        var lying = Body(IngestTestHarness.InstallA, origin: "harvest");
        await client.PostAsJsonAsync("/v1/lines", lying);

        await using var db = factory.NewContext();
        var vote = await db.PendingVotes.SingleAsync();
        Assert.Equal(LineOrigin.Live, vote.Origin);
    }

    [Fact]
    public async Task Missing_or_malformed_install_id_stores_nothing_but_still_answers_200()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        foreach (var bogus in new[] { "", "not-a-guid", "00000000-0000-0000-0000-000000000000" })
        {
            var response = await client.PostAsJsonAsync("/v1/lines", Body(bogus));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using var db = factory.NewContext();
        Assert.Empty(db.PendingLines);
    }
}
