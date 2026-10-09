using System;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.DetectAspectRatio.Client;

/// <summary>
/// Serves the embedded client add-on. It is anonymous because module imports carry no Jellyfin token.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[Route(ClientScript.Route)]
public sealed class ClientScriptController : ControllerBase
{
    /// <summary>
    /// Gets the client add-on module.
    /// </summary>
    /// <param name="version">The content version the bootstrap requests; only a request for the current version may be cached for good.</param>
    /// <returns>The module, or 404 when it is not embedded.</returns>
    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetClientScript([FromQuery(Name = ClientScript.VersionParameter)] string? version)
    {
        string? currentVersion = ClientScript.Version;
        if (currentVersion is null)
        {
            return NotFound();
        }

        Stream? stream = ClientScript.Open();
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = string.Equals(version, currentVersion, StringComparison.Ordinal)
            ? ClientScript.ImmutableCacheControl
            : ClientScript.RevalidateCacheControl;
        return File(stream, ClientScript.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue("\"" + currentVersion + "\""));
    }
}
