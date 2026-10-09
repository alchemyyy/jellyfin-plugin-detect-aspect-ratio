namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// The measured black bars of one video.
/// </summary>
/// <param name="MethodVersion">The version of the measuring method, <see cref="BlackBarAccumulator.MethodVersion"/> when it was taken.</param>
/// <param name="Source">The trickplay images the measurement was taken from.</param>
/// <param name="AspectRatio">The width to height ratio of the picture inside the black bars, or 0 when the video has none.</param>
public sealed record BlackBarMeasurement(int MethodVersion, TrickplaySource Source, double AspectRatio)
{
    /// <summary>
    /// Returns whether the measurement still holds: taken by the current method from the video's current trickplay images.
    /// </summary>
    /// <param name="source">The video's current trickplay images.</param>
    /// <returns><c>true</c> when the measurement does not need to be taken again.</returns>
    public bool IsCurrent(TrickplaySource source)
    {
        return MethodVersion == BlackBarAccumulator.MethodVersion && Source == source;
    }
}
