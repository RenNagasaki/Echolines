using System.Net;
using System.Net.Http.Json;
using Echolines.Server.Models;
using Microsoft.AspNetCore.Hosting;

namespace Echolines.Tests;

/// <summary>
/// The forwarded-headers switch, tested through the effect that matters rather than through the
/// middleware directly: whether two callers behind the same proxy get their own rate-limit budget.
/// Both settings fail quietly — nothing throws either way, the limiter simply stops telling callers
/// apart — which is exactly why this is a test and not a comment.
/// </summary>
public class ReverseProxyTests
{
    private const int Permit = 3;

    private sealed class ProxyFactory(bool behindProxy) : EcholinesFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Server:BehindReverseProxy", behindProxy ? "true" : "false");
            builder.UseSetting("RateLimit:LivePermitPerMinute", Permit.ToString());
        }
    }

    /// <summary>Sends one report as <paramref name="forwardedFor"/> and returns the status.</summary>
    private static async Task<HttpStatusCode> Report(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/lines")
        {
            // No X-Install-Id on purpose: without it the limiter partitions on the address alone,
            // which is precisely the value the forwarded header decides.
            Content = JsonContent.Create(new SubmitRequest
            {
                InstallId = IngestTestHarness.InstallA,
                Lines = [IngestTestHarness.Line()],
            }),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);

        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task With_the_switch_on_two_callers_behind_one_proxy_get_separate_budgets()
    {
        using var factory = new ProxyFactory(behindProxy: true);
        using var client = factory.CreateClient();

        for (var i = 0; i < Permit; i++)
            Assert.Equal(HttpStatusCode.OK, await Report(client, "203.0.113.7"));

        Assert.Equal(HttpStatusCode.TooManyRequests, await Report(client, "203.0.113.7"));

        // The second address is untouched by the first one's spending — the whole point.
        Assert.Equal(HttpStatusCode.OK, await Report(client, "198.51.100.4"));
    }

    [Fact]
    public async Task With_the_switch_off_the_forwarded_header_buys_nothing()
    {
        // The safe default for a directly exposed port: honouring the header would let one caller
        // invent a fresh address per request and walk straight around the limit.
        using var factory = new ProxyFactory(behindProxy: false);
        using var client = factory.CreateClient();

        for (var i = 0; i < Permit; i++)
            Assert.Equal(HttpStatusCode.OK, await Report(client, "203.0.113.7"));

        Assert.Equal(HttpStatusCode.TooManyRequests, await Report(client, "203.0.113.7"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await Report(client, "198.51.100.4"));
    }
}
