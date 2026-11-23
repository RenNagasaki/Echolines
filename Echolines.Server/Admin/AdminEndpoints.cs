using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Echolines.Server.Admin;

public static class AdminEndpoints
{
    public const string CookieScheme = "EcholinesAdmin";
    public const string LoginRateLimitPolicy = "admin-login";
    public const string LoginPath = "/admin/login";

    /// <summary>
    /// The POST target, deliberately NOT the page path. A Razor component route answers POST as well
    /// since .NET 8 (that is how static form handling works), so a handler mapped onto the page path is
    /// a second equally specific match and routing throws AmbiguousMatchException — a 500 on every
    /// sign-in attempt, right password or wrong.
    /// </summary>
    public const string SignInPath = "/admin/signin";

    public const string SignOutPath = "/admin/logout";

    /// <summary>
    /// Sign-in and sign-out live in plain form-post endpoints rather than in the Blazor circuit:
    /// issuing a cookie needs the real HTTP response, which an interactive component no longer has.
    /// </summary>
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(SignInPath, async (
                HttpContext http,
                IOptions<AdminOptions> options,
                ILoggerFactory loggerFactory) =>
            {
                var form = await http.Request.ReadFormAsync();
                var password = form["password"].ToString();
                var log = loggerFactory.CreateLogger("Echolines.Admin");

                if (string.IsNullOrEmpty(options.Value.PasswordHash))
                {
                    log.LogWarning("Admin login attempted but no Admin:PasswordHash is configured");
                    return Results.Redirect($"{LoginPath}?error=unconfigured");
                }

                if (!AdminPasswordHasher.Verify(password, options.Value.PasswordHash))
                {
                    log.LogWarning("Failed admin login");
                    return Results.Redirect($"{LoginPath}?error=1");
                }

                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "operator")],
                    CookieScheme);

                await http.SignInAsync(
                    CookieScheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.Add(options.Value.SessionLifetime),
                    });

                return Results.Redirect("/admin");
            })
            .RequireRateLimiting(LoginRateLimitPolicy);

        // No page lives at this path, so there is nothing here to collide with.
        app.MapPost(SignOutPath, async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieScheme);
            return Results.Redirect(LoginPath);
        });
    }

    public static void AddAdminAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieScheme)
            .AddCookie(CookieScheme, o =>
            {
                o.LoginPath = LoginPath;
                o.LogoutPath = SignOutPath;
                o.AccessDeniedPath = LoginPath;
                o.Cookie.Name = CookieScheme;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // Always over TLS in production; the container itself speaks HTTP behind a reverse proxy,
                // so this is left to the proxy rather than forced here and breaking local runs.
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
    }
}
