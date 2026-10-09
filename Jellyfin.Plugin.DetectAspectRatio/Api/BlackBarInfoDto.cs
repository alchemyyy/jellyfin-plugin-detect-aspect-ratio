using System;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;

namespace Jellyfin.Plugin.DetectAspectRatio.Api;

/// <summary>
/// The black bars of a video, as the client add-on reads them.
/// </summary>
/// <param name="ItemId">The video.</param>
/// <param name="IsMeasured">Whether the video was measured. It is not when it has no readable trickplay images.</param>
/// <param name="AspectRatio">The measured width to height ratio of the picture inside the black bars, or 0 when the video has none.</param>
/// <param name="SnappedAspectRatio">The industry-standard ratio nearest the measurement, which players crop to, or 0 when none is close enough.</param>
/// <param name="SnappedAspectRatioName">The name of the snapped ratio, or <c>null</c> when there is none.</param>
public sealed record BlackBarInfoDto(
    Guid ItemId,
    bool IsMeasured,
    double AspectRatio,
    double SnappedAspectRatio,
    string? SnappedAspectRatioName)
{
    /// <summary>
    /// Describes the measurement of a video.
    /// </summary>
    /// <param name="itemId">The video.</param>
    /// <param name="measurement">The measurement, or <c>null</c> when the video could not be measured.</param>
    /// <returns>The black bars of the video.</returns>
    public static BlackBarInfoDto Create(Guid itemId, BlackBarMeasurement? measurement)
    {
        if (measurement is null)
        {
            return new BlackBarInfoDto(itemId, false, 0, 0, null);
        }

        // A video without black bars measures 0, which no standard ratio is near
        StandardAspectRatio? snapped = AspectRatioFunctions.Snap(measurement.AspectRatio);
        return new BlackBarInfoDto(itemId, true, measurement.AspectRatio, snapped?.Ratio ?? 0, snapped?.Name);
    }
}
