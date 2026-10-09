using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace Jellyfin.Plugin.DetectAspectRatio.Transformations;

/// <summary>
/// Stateless rewrites of the Jellyfin Web files that load the client add-on.
/// </summary>
public static class WebClientFunctions
{
    /// <summary>
    /// The plugin name added to <c>config.json</c>. Jellyfin Web resolves it to the window factory of the same name.
    /// </summary>
    public const string ClientPluginName = "DetectAspectRatio";

    /// <summary>
    /// The attribute that marks the injected bootstrap scripts.
    /// </summary>
    public const string BootstrapMarker = "data-detect-aspect-ratio-bootstrap";

    /// <summary>
    /// The window property the module script sets to a function that imports a module.
    /// </summary>
    public const string ModuleImportName = "DetectAspectRatioImport";

    /// <summary>
    /// How long the window factory waits for the add-on import. A factory that never settles blocks the first render.
    /// </summary>
    public const int AddonLoadTimeoutMilliseconds = 20000;

    private const string HeadCloseTag = "</head>";
    private const string PluginsProperty = "plugins";

    // NOTE: A classic script that spelled out import() would not parse in the older TV browsers Jellyfin Web supports, and neither
    // would the rest of it. Only browsers with dynamic import run this module script, and Jellyfin's documented CSP allows it
    private const string ModuleImportScript = $$"""
        <script type="module" {{BootstrapMarker}}>window.{{ModuleImportName}} = function (url) { return import(url); };</script>
        """;

    // NOTE: Classic inline script in ES5, because it runs before Jellyfin Web's polyfills; the settings object literal is appended as the IIFE argument.
    // The factory always resolves, to a stand-in plugin where the add-on cannot load, since a rejection makes Jellyfin Web render before its other plugins load
    private const string BootstrapScriptStart = $$"""
        <script {{BootstrapMarker}}>
        (function (settings) {
            'use strict';
            var pathname = window.location.pathname;
            var webIndex = pathname.lastIndexOf('/web/');
            var base = webIndex === -1 ? '' : pathname.substring(0, webIndex);
            var documentLoaded = false;
            var pendingLoads = [];

            function UnavailableAddon() {
                this.name = 'Detect Aspect Ratio';
                this.id = 'detectaspectratio';
            }

            function loadAddon() {
                var importModule = window.{{ModuleImportName}};
                if (typeof importModule !== 'function') {
                    console.warn('Detect Aspect Ratio is unavailable: this browser cannot import JavaScript modules');
                    return Promise.resolve(UnavailableAddon);
                }

                var timer;
                var timeout = new Promise(function (resolve, reject) {
                    timer = setTimeout(function () {
                        reject(new Error('the add-on did not load within ' + settings.loadTimeout + ' ms'));
                    }, settings.loadTimeout);
                });
                var load = importModule(base + settings.entry).then(function (module) {
                    return module.default;
                });
                return Promise.race([load, timeout]).then(function (addon) {
                    clearTimeout(timer);
                    return addon;
                }, function (error) {
                    clearTimeout(timer);
                    console.error('Detect Aspect Ratio is unavailable:', error);
                    return UnavailableAddon;
                });
            }

            // Module scripts run before DOMContentLoaded, so a factory called earlier waits for it
            document.addEventListener('DOMContentLoaded', function () {
                documentLoaded = true;
                pendingLoads.forEach(function (load) {
                    load();
                });
            });

            window.{{ClientPluginName}} = function () {
                return new Promise(function (resolve) {
                    if (documentLoaded) {
                        resolve(loadAddon());
                        return;
                    }

                    pendingLoads.push(function () {
                        resolve(loadAddon());
                    });
                });
            };
        })(
        """;

    private const string BootstrapScriptEnd = ");</script>";

    private static readonly JsonSerializerOptions ScriptSerializerOptions = new JsonSerializerOptions
    {
        // The default encoder escapes <, >, &, quotes, + and U+2028/U+2029, so no value can end the inline script early
        Encoder = JavaScriptEncoder.Default,
    };

    private static readonly JsonSerializerOptions ConfigSerializerOptions = new JsonSerializerOptions
    {
        // config.json is served as JSON and never inlined into HTML, so non-ASCII text stays literal
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        NewLine = "\n",
        WriteIndented = true,
    };

    private static readonly JsonDocumentOptions StrictDocumentOptions = new JsonDocumentOptions { AllowDuplicateProperties = false };

    /// <summary>
    /// Inserts the bootstrap scripts immediately before the first <c>&lt;/head&gt;</c>, matched case-insensitively.
    /// </summary>
    /// <param name="html">The served <c>index.html</c>.</param>
    /// <param name="entry">The add-on module path below the server base URL, or <c>null</c> when no add-on is embedded.</param>
    /// <returns>The rewritten page, or <paramref name="html"/> unchanged when there is no add-on, no head end tag, or a bootstrap already.</returns>
    public static string InjectBootstrapScript(string html, string? entry)
    {
        if (string.IsNullOrEmpty(entry))
        {
            return html;
        }

        if (html.Contains(BootstrapMarker, StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        int headCloseIndex = html.IndexOf(HeadCloseTag, StringComparison.OrdinalIgnoreCase);
        if (headCloseIndex < 0)
        {
            return html;
        }

        return html.Insert(headCloseIndex, BuildBootstrapScript(entry));
    }

    /// <summary>
    /// Builds the inline scripts that define the window factory, which imports the add-on module.
    /// </summary>
    /// <param name="entry">The add-on module path below the server base URL.</param>
    /// <returns>The module script that can import modules, followed by the classic script that defines the factory.</returns>
    public static string BuildBootstrapScript(string entry)
    {
        JsonObject settings = new JsonObject
        {
            ["entry"] = entry,
            ["loadTimeout"] = AddonLoadTimeoutMilliseconds,
        };

        return ModuleImportScript + BootstrapScriptStart + settings.ToJsonString(ScriptSerializerOptions) + BootstrapScriptEnd;
    }

    /// <summary>
    /// Appends <see cref="ClientPluginName"/> to the <c>plugins</c> array of <c>config.json</c>.
    /// </summary>
    /// <param name="configJSON">The served <c>config.json</c>.</param>
    /// <returns>The rewritten JSON, or <paramref name="configJSON"/> unchanged when it is malformed, has no <c>plugins</c> array, or already lists the plugin.</returns>
    public static string AddPluginToConfig(string configJSON)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(configJSON, documentOptions: StrictDocumentOptions);
        }
        catch (JsonException)
        {
            // Serve malformed JSON as is; Jellyfin Web reports it and uses its defaults
            return configJSON;
        }

        // NOTE: Never create the array, because a missing key is what makes Jellyfin Web use its built-in plugin list
        if (root is not JsonObject rootObject
            || !rootObject.TryGetPropertyValue(PluginsProperty, out JsonNode? pluginsNode)
            || pluginsNode is not JsonArray plugins)
        {
            return configJSON;
        }

        foreach (JsonNode? plugin in plugins)
        {
            if (plugin is JsonValue pluginValue
                && pluginValue.TryGetValue(out string? pluginName)
                && string.Equals(pluginName, ClientPluginName, StringComparison.Ordinal))
            {
                return configJSON;
            }
        }

        plugins.Add(JsonValue.Create(ClientPluginName));
        return rootObject.ToJsonString(ConfigSerializerOptions);
    }
}
