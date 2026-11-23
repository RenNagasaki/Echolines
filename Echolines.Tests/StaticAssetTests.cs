using System.Net;
using System.Text.RegularExpressions;

namespace Echolines.Tests;

/// <summary>
/// Every asset the pages reference has to be served by something.
/// </summary>
/// <remarks>
/// The admin UI shipped once with <c>/_framework/blazor.web.js</c> answering 404, which left every
/// control on it inert while the pages themselves rendered perfectly. Nothing in the suite noticed,
/// because no test had ever asked for a file. The Dockerfile carries the matching assertion for the
/// published image, where that particular failure actually happened.
/// </remarks>
public partial class StaticAssetTests
{
    [GeneratedRegex("""(?:src|href)="(?!https?:|//)([^"#?]+)""")]
    private static partial Regex AssetReference();

    [Theory]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/app.css")]
    public async Task The_assets_the_layout_depends_on_are_served(string path)
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Content.Headers.ContentLength > 0, $"{path} was served empty");
    }

    [Fact]
    public async Task Nothing_the_login_page_references_is_missing()
    {
        using var factory = new EcholinesFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/admin/login");

        var referenced = AssetReference().Matches(html)
            .Select(m => m.Groups[1].Value)
            .Where(v => v.Length > 0 && v != "/")
            .Distinct()
            .ToList();

        Assert.NotEmpty(referenced);

        foreach (var asset in referenced)
        {
            var response = await client.GetAsync(asset.StartsWith('/') ? asset : $"/{asset}");
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"the page references {asset}, which answers {(int)response.StatusCode}");
        }
    }
}
