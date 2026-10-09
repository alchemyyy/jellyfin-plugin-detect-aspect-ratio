using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DetectAspectRatio.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class ClientScriptTests
{
    [Fact]
    public void EmbeddedModule_IsTheAddonWithAContentVersion()
    {
        using Stream? stream = ClientScript.Open();
        Assert.NotNull(stream);
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        string module = reader.ReadToEnd();

        Assert.Contains("export default function DetectAspectRatio(bag)", module, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{16}$", ClientScript.Version);
        Assert.Equal("/DetectAspectRatio/Client/detectAspectRatio.js?v=" + ClientScript.Version, ClientScript.EntryPath);
    }

    [Fact]
    public void EmbeddedModule_UsesNoSyntaxNewerThanES2017()
    {
        using StreamReader reader = new StreamReader(ClientScript.Open()!, Encoding.UTF8);
        string module = reader.ReadToEnd();

        // Optional chaining and nullish coalescing need Chrome 80, but dynamic import, the add-on's only requirement, arrived in Chrome 63
        Assert.DoesNotContain("?.", module, StringComparison.Ordinal);
        Assert.DoesNotContain("??", module, StringComparison.Ordinal);
        Assert.DoesNotContain("catch {", module, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetClientScript_CurrentVersion_IsCachedForGood()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using WebApplication application = await StartServerAsync(cancellationToken);
        using HttpClient client = application.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(ClientScript.EntryPath!, UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ClientScript.ImmutableCacheControl, string.Join(", ", response.Headers.NonValidated[HeaderNames.CacheControl]));
        Assert.Equal("\"" + ClientScript.Version + "\"", response.Headers.ETag?.ToString());
        Assert.Equal(await ReadEmbeddedAsync(cancellationToken), await response.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData("/DetectAspectRatio/Client/detectAspectRatio.js")]
    [InlineData("/DetectAspectRatio/Client/detectAspectRatio.js?v=0000000000000000")]
    public async Task GetClientScript_OtherVersion_IsRevalidated(string requestPath)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using WebApplication application = await StartServerAsync(cancellationToken);
        using HttpClient client = application.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri(requestPath, UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ClientScript.RevalidateCacheControl, string.Join(", ", response.Headers.NonValidated[HeaderNames.CacheControl]));
    }

    [Fact]
    public async Task GetClientScript_MatchingETag_ReturnsNotModified()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using WebApplication application = await StartServerAsync(cancellationToken);
        using HttpClient client = application.GetTestClient();
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri("/DetectAspectRatio/Client/detectAspectRatio.js", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, "\"" + ClientScript.Version + "\"");

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
    }

    private static async Task<WebApplication> StartServerAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ClientScriptController).Assembly);
        WebApplication application = builder.Build();
        application.MapControllers();
        await application.StartAsync(cancellationToken);
        return application;
    }

    private static async Task<byte[]> ReadEmbeddedAsync(CancellationToken cancellationToken)
    {
        await using Stream stream = ClientScript.Open()!;
        using MemoryStream copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }
}
