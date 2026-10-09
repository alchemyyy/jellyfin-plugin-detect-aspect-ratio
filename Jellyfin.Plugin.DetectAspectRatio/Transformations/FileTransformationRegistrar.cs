using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DetectAspectRatio.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DetectAspectRatio.Transformations;

/// <summary>
/// Decides at server start which component rewrites Jellyfin Web: File Transformation when it is installed and accepts
/// the callbacks, otherwise the plugin's own <see cref="WebClientRewriteMiddleware"/>. Never both.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger) : IHostedService
{
    /// <summary>
    /// The exact key of the web client page. File Transformation runs one rule list per file, and exact keys are shared and win over regex keys.
    /// </summary>
    public const string IndexHTMLFileName = "index.html";

    /// <summary>
    /// The exact key of the web client configuration.
    /// </summary>
    public const string ConfigJSONFileName = "config.json";

    private static readonly Guid IndexHTMLTransformationId = new Guid("244be031-be20-4da7-a453-92d44618706e");
    private static readonly Guid ConfigJSONTransformationId = new Guid("49a5e968-dde6-400c-acba-ff7d3a7af8b5");

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (ClientScript.EntryPath is null)
        {
            // The callbacks inject nothing without the module, so either rewriter leaves Jellyfin Web stock
            logger.LogWarning("Detect Aspect Ratio client add-on is not embedded; Jellyfin Web stays unmodified");
        }

        WebClientRewriteMode mode;
        try
        {
            mode = DecideRewriteMode();
        }
        catch (Exception exception)
        {
            // NOTE: An exception here aborts server start, so it only logs; registration failures are caught before this point
            logger.LogWarning(exception, "Detect Aspect Ratio could not set up File Transformation; its own middleware rewrites Jellyfin Web");
            mode = WebClientRewriteMode.Middleware;
        }

        WebClientRewriteState.SetMode(mode);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private WebClientRewriteMode DecideRewriteMode()
    {
        MethodInfo? registerMethod = FileTransformationFunctions.FindRegisterMethod(AssemblyLoadContext.All.SelectMany(context => context.Assemblies));
        if (registerMethod is null)
        {
            logger.LogInformation("File Transformation is not installed; Detect Aspect Ratio rewrites index.html and config.json with its own middleware");
            return WebClientRewriteMode.Middleware;
        }

        // NOTE: index.html goes first, because config.json must never name a window factory that index.html does not define
        if (!TryRegisterCallback(registerMethod, IndexHTMLTransformationId, IndexHTMLFileName, nameof(TransformationCallbacks.TransformIndexHTML)))
        {
            logger.LogWarning("File Transformation rejected the Detect Aspect Ratio callbacks; its own middleware rewrites Jellyfin Web");
            return WebClientRewriteMode.Middleware;
        }

        if (TryRegisterCallback(registerMethod, ConfigJSONTransformationId, ConfigJSONFileName, nameof(TransformationCallbacks.TransformConfigJSON)))
        {
            logger.LogInformation("Detect Aspect Ratio rewrites Jellyfin Web through File Transformation");
            return WebClientRewriteMode.FileTransformation;
        }

        // The middleware may only take over once File Transformation no longer rewrites index.html too
        if (TryRemoveCallback(registerMethod, IndexHTMLTransformationId))
        {
            logger.LogWarning("File Transformation rejected the Detect Aspect Ratio config.json callback; its own middleware rewrites Jellyfin Web");
            return WebClientRewriteMode.Middleware;
        }

        logger.LogError("Detect Aspect Ratio could neither complete nor withdraw its File Transformation registration; Jellyfin Web will not load the add-on");
        return WebClientRewriteMode.FileTransformation;
    }

    private bool TryRegisterCallback(MethodInfo registerMethod, Guid id, string fileName, string callbackMethod)
    {
        try
        {
            string registrationJSON = FileTransformationFunctions.BuildRegistrationJSON(id, fileName, typeof(TransformationCallbacks), callbackMethod);
            object payload = FileTransformationFunctions.CreatePayload(registerMethod, registrationJSON);
            registerMethod.Invoke(null, [payload]);
            logger.LogInformation("Detect Aspect Ratio registered its File Transformation callback for {FileName}", fileName);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "File Transformation rejected the Detect Aspect Ratio callback for {FileName}", fileName);
            return false;
        }
    }

    private bool TryRemoveCallback(MethodInfo registerMethod, Guid id)
    {
        MethodInfo? removeMethod = FileTransformationFunctions.FindRemoveMethod(registerMethod);
        if (removeMethod is null)
        {
            return false;
        }

        try
        {
            removeMethod.Invoke(null, [id]);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "File Transformation could not remove the Detect Aspect Ratio callback {Id}", id);
            return false;
        }
    }
}
