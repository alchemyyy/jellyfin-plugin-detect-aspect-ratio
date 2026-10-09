using System.IO;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using Jellyfin.Plugin.DetectAspectRatio.Transformations;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DetectAspectRatio;

/// <summary>
/// Registers the plugin's services with the server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(serviceProvider => new BlackBarStore(
            Path.Combine(serviceProvider.GetRequiredService<IApplicationPaths>().DataPath, BlackBarStore.DirectoryName),
            serviceProvider.GetRequiredService<ILogger<BlackBarStore>>()));
        serviceCollection.AddSingleton<BlackBarAnalyzer>();
        serviceCollection.AddHostedService<TrickplayTaskListener>();

        // The registrar picks File Transformation or the fallback middleware; the middleware is always installed and stays inert unless picked
        serviceCollection.AddHostedService<FileTransformationRegistrar>();
        serviceCollection.AddTransient<IStartupFilter, WebClientRewriteStartupFilter>();
    }
}
