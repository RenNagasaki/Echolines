using System.Reflection;
using Echolines.Server.Data.Entities;
using Echolines.Server.Models;
using Echolines.Server.Services;

namespace Echolines.Tests;

/// <summary>
/// Privacy as a test rather than as an intention. The changelog tells every user that nothing about
/// them is transmitted or stored; these are the assertions that keep that sentence true when somebody
/// adds a field in a hurry two years from now.
/// </summary>
public class PrivacyTests
{
    private static readonly string[] ForbiddenFieldNames =
    [
        "characterName", "playerName", "contentId", "world", "server", "account",
        "playTime", "path", "filePath", "hwid", "machineId", "ip", "ipAddress",
    ];

    [Fact]
    public void The_wire_format_has_no_field_that_could_carry_user_data()
    {
        AssertNoForbiddenProperties(typeof(SubmitRequest));
        AssertNoForbiddenProperties(typeof(SubmitLine));
    }

    [Fact]
    public void Nothing_stored_next_to_a_line_identifies_a_person()
    {
        AssertNoForbiddenProperties(typeof(Line));
        AssertNoForbiddenProperties(typeof(PendingLine));

        // The vote carries the installation GUID and nothing else — no IP, no timestamp of play,
        // nothing that could be joined back to a person.
        AssertNoForbiddenProperties(typeof(PendingVote));
    }

    [Fact]
    public void Install_id_is_only_ever_accepted_as_a_guid()
    {
        // Free text here would turn the vote key into a place where a stray character name could be
        // parked, deliberately or by accident. Only a parseable, non-empty GUID gets through.
        var normalise = typeof(LineIngestService)
            .GetMethod("NormaliseInstallId", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Null(normalise.Invoke(null, ["Cloud Strife@Odin"]));
        Assert.Null(normalise.Invoke(null, ["00000000-0000-0000-0000-000000000000"]));
        Assert.Null(normalise.Invoke(null, [null]));
        Assert.NotNull(normalise.Invoke(null, [IngestTestHarness.InstallA]));
    }

    private static void AssertNoForbiddenProperties(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            var match = ForbiddenFieldNames.FirstOrDefault(
                f => property.Name.Equals(f, StringComparison.OrdinalIgnoreCase));

            Assert.True(match is null,
                $"{type.Name}.{property.Name} looks like user data — the payload carries three things: " +
                "the line, its language, and which NPC says it.");
        }
    }
}
