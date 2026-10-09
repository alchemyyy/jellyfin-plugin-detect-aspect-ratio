using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DetectAspectRatio;

/// <summary>
/// The Detect Aspect Ratio plugin. It has no server settings: the aspect ratio choice is the player's own, kept per browser.
/// </summary>
/// <remarks>
/// The empty configuration base is still required: only it records the assembly path and version, which the server dereferences when it creates and lists plugins.
/// </remarks>
public class Plugin : BasePlugin<BasePluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The server's application paths.</param>
    /// <param name="xmlSerializer">The server's XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Detect Aspect Ratio";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("d690f47c-0540-4d33-b141-26948ccf1f35");

    /// <inheritdoc />
    public override string Description => "Crops black bars encoded into videos when Jellyfin Web plays them.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }
}
