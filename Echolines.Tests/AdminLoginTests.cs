using System.Net;
using Echolines.Server.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Echolines.Tests;

/// <summary>
/// The sign-in round trip, driven through the real pipeline with a real POST.
/// </summary>
/// <remarks>
/// This file exists because its absence let a 500 reach production: every other admin test either
/// issued a GET or called <see cref="AdminPasswordHasher"/> directly, so nothing ever routed a POST —
/// and routing was exactly what was broken. A form endpoint that is only tested through its helper
/// is not tested.
/// </remarks>
public class AdminLoginTests
{
    private const string Password = "correct horse battery staple";

    private sealed class LoginFactory : EcholinesFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Admin:PasswordHash", AdminPasswordHasher.Hash(Password, iterations: 1000));
        }
    }

    private static HttpClient NoRedirects(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> SignIn(HttpClient client, string password)
    {
        var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("password", password)]);
        return await client.PostAsync(AdminEndpoints.SignInPath, form);
    }

    [Fact]
    public async Task The_right_password_signs_in_and_redirects_to_the_admin_home()
    {
        using var factory = new LoginFactory();
        using var client = NoRedirects(factory);

        var response = await SignIn(client, Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin", response.Headers.Location!.OriginalString);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith(AdminEndpoints.CookieScheme, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_cookie_from_a_successful_sign_in_actually_opens_the_admin_pages()
    {
        using var factory = new LoginFactory();
        using var client = NoRedirects(factory);

        var login = await SignIn(client, Password);
        var cookie = login.Headers.GetValues("Set-Cookie")
            .First(c => c.StartsWith(AdminEndpoints.CookieScheme, StringComparison.Ordinal));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin");
        request.Headers.Add("Cookie", cookie.Split(';')[0]);

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_redirects_back_with_an_error_and_sets_no_cookie()
    {
        using var factory = new LoginFactory();
        using var client = NoRedirects(factory);

        var response = await SignIn(client, "wrong");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=1", response.Headers.Location!.OriginalString);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task With_no_hash_configured_even_the_right_password_is_refused()
    {
        // Fails closed. An empty hash must never be read as "no password required".
        using var factory = new EcholinesFactory();
        using var client = NoRedirects(factory);

        var response = await SignIn(client, Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("error=unconfigured", response.Headers.Location!.OriginalString);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task The_login_page_and_the_sign_in_handler_do_not_fight_over_the_same_route()
    {
        // The regression guard for the actual outage: the page is a Razor component at
        // /admin/login, and since .NET 8 a component route also answers POST for form handling.
        // Mapping the handler onto that same path made every POST an AmbiguousMatchException — a
        // 500 for right and wrong passwords alike, which no GET-only test could see.
        Assert.NotEqual(AdminEndpoints.LoginPath, AdminEndpoints.SignInPath);

        using var factory = new LoginFactory();
        using var client = NoRedirects(factory);

        var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("password", Password)]);
        var pageResponse = await client.PostAsync(AdminEndpoints.LoginPath, form);

        Assert.NotEqual(HttpStatusCode.InternalServerError, pageResponse.StatusCode);
    }
}
