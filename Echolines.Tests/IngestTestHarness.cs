using Echolines.Server.Data;
using Echolines.Server.Data.Entities;
using Echolines.Server.Models;
using Echolines.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Echolines.Tests;

/// <summary>
/// One in-memory SQLite database per test.
/// </summary>
/// <remarks>
/// SQLite rather than the InMemory provider on purpose: <c>UNIQUE(pending_id, voter_key)</c> <i>is</i>
/// the consensus rule, and the InMemory provider ignores unique indexes entirely. A test suite that
/// cannot fail on a broken constraint would certify exactly the bug it exists to catch.
/// </remarks>
public sealed class IngestTestHarness : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IngestOptions _options;

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));

    public IngestTestHarness(int promotionThreshold = 3)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new IngestOptions { PromotionThreshold = promotionThreshold };

        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public EcholinesDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<EcholinesDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new EcholinesDbContext(options);
    }

    /// <summary>Fresh context per call, so a test can never pass on nothing but change-tracker state.</summary>
    public async Task<IngestResult> Submit(
        string installId,
        LineOrigin origin,
        params SubmitLine[] lines)
    {
        await using var db = NewContext();
        var service = new LineIngestService(
            db,
            Options.Create(_options),
            Clock,
            NullLogger<LineIngestService>.Instance);

        return await service.IngestAsync(
            new SubmitRequest { InstallId = installId, Lines = [.. lines] },
            origin,
            maxLines: 1000);
    }

    public Task<IngestResult> SubmitLive(string installId, params SubmitLine[] lines) =>
        Submit(installId, LineOrigin.Live, lines);

    public Task<IngestResult> SubmitHarvest(string installId, params SubmitLine[] lines) =>
        Submit(installId, LineOrigin.Harvest, lines);

    public static SubmitLine Line(
        string text = "Well met, <PLAYERFIRSTNAME>.",
        string lang = "en",
        string npc = "Alphinaud Leveilleur") => new()
    {
        Lang = lang,
        Npc = npc,
        Gender = "Male",
        Race = "Elezen",
        NpcBaseId = 1000123,
        Text = text,
        Hash = LineKey.FromText(text),
        Source = "AddonTalk",
    };

    public static readonly string InstallA = "11111111-1111-1111-1111-111111111111";
    public static readonly string InstallB = "22222222-2222-2222-2222-222222222222";
    public static readonly string InstallC = "33333333-3333-3333-3333-333333333333";

    public void Dispose() => _connection.Dispose();
}
