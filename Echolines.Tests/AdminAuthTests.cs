using System.Net;
using Echolines.Server.Admin;

namespace Echolines.Tests;

/// <summary>
/// The admin surface is the one part of this service that can cause the poisoning the quarantine was
/// built to prevent. An unauthenticated route into it would be the largest single mistake in the design,
/// so "is it actually closed?" is a test rather than a habit.
/// </summary>
public class AdminAuthTests
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/insights")]
    [InlineData("/admin/import")]
    public async Task Admin_pages_send_an_anonymous_visitor_to_the_login(string path)
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/admin/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_login_page_itself_is_reachable()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/login")).StatusCode);
    }

    [Fact]
    public void An_unconfigured_password_hash_refuses_every_login()
    {
        // Empty must not mean "no password required" — that would put an open admin UI on the internet
        // the first time someone forgets to set the secret.
        Assert.False(AdminPasswordHasher.Verify("", ""));
        Assert.False(AdminPasswordHasher.Verify("anything", ""));
        Assert.False(AdminPasswordHasher.Verify("anything", "   "));
    }

    [Fact]
    public void A_hashed_password_verifies_and_a_wrong_one_does_not()
    {
        var hash = AdminPasswordHasher.Hash("correct horse battery staple", iterations: 1000);

        Assert.True(AdminPasswordHasher.Verify("correct horse battery staple", hash));
        Assert.False(AdminPasswordHasher.Verify("Correct horse battery staple", hash));
        Assert.False(AdminPasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Two_hashes_of_one_password_differ_because_the_salt_does()
    {
        var a = AdminPasswordHasher.Hash("same password", iterations: 1000);
        var b = AdminPasswordHasher.Hash("same password", iterations: 1000);

        Assert.NotEqual(a, b);
        Assert.True(AdminPasswordHasher.Verify("same password", a));
        Assert.True(AdminPasswordHasher.Verify("same password", b));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("pbkdf2$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$1000$not-base64!$aGFzaA==")]
    [InlineData("scrypt$1000$c2FsdA==$aGFzaA==")]
    public void A_malformed_stored_hash_fails_closed(string stored) =>
        Assert.False(AdminPasswordHasher.Verify("anything", stored));
}
