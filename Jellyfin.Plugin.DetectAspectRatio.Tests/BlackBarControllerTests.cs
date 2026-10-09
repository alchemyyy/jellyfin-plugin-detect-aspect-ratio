using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.DetectAspectRatio.Api;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

// Serves the controller with Jellyfin's PascalCase JSON, behind an authentication scheme that accepts every request
public sealed class BlackBarControllerTests : IDisposable
{
    private const string TestScheme = "Test";

    private readonly AnalysisFixture fixture = new AnalysisFixture();
    private readonly User user = new User("viewer", "Default", "Default");

    public void Dispose()
    {
        fixture.Dispose();
    }

    [Fact]
    public async Task GetBlackBars_MeasuredVideo_ReturnsTheCropRatio()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Scope Film");
        fixture.AddTrickplay(video, 120, 2.39);
        await using WebApplication application = await StartServerAsync(cancellationToken);
        using HttpClient client = application.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/DetectAspectRatio/Items/" + video.Id.ToString("N"), UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject blackBars = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        Assert.Equal(video.Id, (Guid?)blackBars["ItemId"]);
        Assert.True((bool?)blackBars["IsMeasured"]);
        Assert.Equal(2.39, (double?)blackBars["SnappedAspectRatio"]);
        Assert.Equal("Modern Anamorphic Scope", (string?)blackBars["SnappedAspectRatioName"]);
        Assert.NotNull(fixture.Store.Read(video.Id));
    }

    [Fact]
    public async Task GetBlackBars_VideoWithoutTrickplay_IsNotMeasured()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Unscanned Film");
        await using WebApplication application = await StartServerAsync(cancellationToken);
        using HttpClient client = application.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/DetectAspectRatio/Items/" + video.Id.ToString("D"), UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((bool?)JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!["IsMeasured"]);
    }

    [Fact]
    public async Task GetBlackBars_UnknownItem_ReturnsNotFound()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using WebApplication application = await StartServerAsync(cancellationToken);
        using HttpClient client = application.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/DetectAspectRatio/Items/" + Guid.NewGuid().ToString("N"), UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBlackBars_ItemHiddenFromTheUser_ReturnsNotFoundWithoutMeasuring()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Restricted Film");
        fixture.AddTrickplay(video, 120, 2.39);
        fixture.HiddenFromUser = user;
        await using WebApplication application = await StartServerAsync(cancellationToken);
        using HttpClient client = application.GetTestClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/DetectAspectRatio/Items/" + video.Id.ToString("N"), UriKind.Relative), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(fixture.Store.Read(video.Id));
    }

    private async Task<WebApplication> StartServerAsync(CancellationToken cancellationToken)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services
            .AddControllers()
            .AddApplicationPart(typeof(BlackBarController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);
        builder.Services.AddAuthentication(TestScheme).AddScheme<AuthenticationSchemeOptions, AcceptingAuthenticationHandler>(TestScheme, null);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(fixture.LibraryManager);
        builder.Services.AddSingleton(fixture.Analyzer);
        builder.Services.AddSingleton(InterfaceFake.Create<IAuthorizationContext>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["GetAuthorizationInfo"] = (method, args) => Task.FromResult(new AuthorizationInfo { User = user, IsAuthenticated = true }),
        }));
        WebApplication application = builder.Build();
        application.UseAuthentication();
        application.UseAuthorization();
        application.MapControllers();
        await application.StartAsync(cancellationToken);
        return application;
    }

    private sealed class AcceptingAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsPrincipal principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "viewer")], TestScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestScheme)));
        }
    }
}
