namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// An industry-standard aspect ratio.
/// </summary>
/// <param name="Ratio">The width to height ratio.</param>
/// <param name="Name">The common name.</param>
public readonly record struct StandardAspectRatio(double Ratio, string Name);
