using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DetectAspectRatio.Api;

/// <summary>
/// Serves the black bars of videos to the client add-on.
/// </summary>
/// <param name="libraryManager">The library manager.</param>
/// <param name="authorizationContext">The authorization context, for the requesting user.</param>
/// <param name="analyzer">The analyzer.</param>
[ApiController]
[Authorize]
[Route(RoutePrefix)]
public sealed class BlackBarController(
    ILibraryManager libraryManager,
    IAuthorizationContext authorizationContext,
    BlackBarAnalyzer analyzer) : ControllerBase
{
    /// <summary>
    /// The route prefix of the plugin API, below the server base URL.
    /// </summary>
    public const string RoutePrefix = "DetectAspectRatio";

    /// <summary>
    /// Gets the black bars of a video, measuring them first when its trickplay images were never measured or changed since.
    /// </summary>
    /// <param name="itemId">The video; for one version of a multi-version item, its media source id.</param>
    /// <returns>The black bars, or 404 when the item is not a video the user can see.</returns>
    [HttpGet("Items/{itemId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BlackBarInfoDto>> GetBlackBars([FromRoute] Guid itemId)
    {
        AuthorizationInfo authorization = await authorizationContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
        Video? video = libraryManager.GetItemById<Video>(itemId, authorization.User);
        if (video is null)
        {
            return NotFound();
        }

        BlackBarMeasurement? measurement = await analyzer.GetMeasurementAsync(video, HttpContext.RequestAborted).ConfigureAwait(false);
        return BlackBarInfoDto.Create(itemId, measurement);
    }
}
