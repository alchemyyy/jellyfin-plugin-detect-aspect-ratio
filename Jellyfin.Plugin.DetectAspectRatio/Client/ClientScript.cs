using System;
using System.IO;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.DetectAspectRatio.Client;

/// <summary>
/// The client add-on module embedded in this assembly, read once on first use.
/// </summary>
public static class ClientScript
{
    /// <summary>
    /// The module route below the server base URL. It must never contain <c>/web/</c>, because File Transformation intercepts every such path and handles text only.
    /// </summary>
    public const string Route = "DetectAspectRatio/Client/detectAspectRatio.js";

    /// <summary>
    /// The query parameter that carries the content version, so every module change gets a new URL.
    /// </summary>
    public const string VersionParameter = "v";

    /// <summary>
    /// The logical name of the embedded module, set in the project file.
    /// </summary>
    public const string ResourceName = "DetectAspectRatio.Client/detectAspectRatio.js";

    /// <summary>
    /// The content type of the module.
    /// </summary>
    public const string ContentType = "text/javascript";

    /// <summary>
    /// The cache policy of a request that names the current version.
    /// </summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>
    /// The cache policy of any other request.
    /// </summary>
    public const string RevalidateCacheControl = "no-cache";

    private const int VersionHashLength = 8;

    // NOTE: Static initializers run in textual order, and the version reads the content
    private static readonly byte[]? ModuleContent = LoadContent();

    /// <summary>
    /// Gets the content version, the first eight bytes of the module's SHA-256 hash in hex, or <c>null</c> when the module is not embedded.
    /// </summary>
    public static string? Version { get; } = ModuleContent is null ? null : Convert.ToHexStringLower(SHA256.HashData(ModuleContent).AsSpan(0, VersionHashLength));

    /// <summary>
    /// Gets the module path the bootstrap imports, below the server base URL, or <c>null</c> when the module is not embedded.
    /// </summary>
    public static string? EntryPath { get; } = Version is null ? null : "/" + Route + "?" + VersionParameter + "=" + Version;

    /// <summary>
    /// Opens the embedded module.
    /// </summary>
    /// <returns>A read-only stream of the module, or <c>null</c> when the module is not embedded.</returns>
    public static Stream? Open()
    {
        return ModuleContent is null ? null : new MemoryStream(ModuleContent, writable: false);
    }

    private static byte[]? LoadContent()
    {
        using Stream? stream = typeof(ClientScript).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using MemoryStream content = new MemoryStream();
        stream.CopyTo(content);
        return content.ToArray();
    }
}
