using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Snaps measured aspect ratios to industry-standard ones.
/// </summary>
public static class AspectRatioFunctions
{
    /// <summary>
    /// The largest difference between a measured ratio and a standard ratio that still snaps to it. A measurement further from every standard matches none.
    /// </summary>
    public const double SnapTolerance = 0.075;

    /// <summary>
    /// Gets the industry-standard aspect ratios in ascending order.
    /// </summary>
    public static IReadOnlyList<StandardAspectRatio> StandardRatios { get; } =
    [
        new StandardAspectRatio(1.00, "1:1 Square"),
        new StandardAspectRatio(1.19, "Movietone"),
        new StandardAspectRatio(1.33, "4:3 Standard TV"),
        new StandardAspectRatio(1.37, "Academy Ratio"),
        new StandardAspectRatio(1.43, "IMAX GT"),
        new StandardAspectRatio(1.50, "VistaVision (3:2)"),
        new StandardAspectRatio(1.56, "14:9 Transition Broadcast"),
        new StandardAspectRatio(1.66, "European Widescreen"),
        new StandardAspectRatio(1.75, "7:4 Widescreen"),
        new StandardAspectRatio(1.78, "16:9 HDTV"),
        new StandardAspectRatio(1.85, "US Widescreen (Flat)"),
        new StandardAspectRatio(1.90, "IMAX Digital"),
        new StandardAspectRatio(2.00, "Univisium"),
        new StandardAspectRatio(2.06, "18.5:9 Digital/Smartphone"),
        new StandardAspectRatio(2.11, "19:9 Smartphone / ARRI Alexa 65"),
        new StandardAspectRatio(2.20, "70mm Standard"),
        new StandardAspectRatio(2.22, "20:9 Ultrawide Smartphone"),
        new StandardAspectRatio(2.33, "21:9 Ultrawide Monitor"),
        new StandardAspectRatio(2.35, "Early Anamorphic Scope"),
        new StandardAspectRatio(2.39, "Modern Anamorphic Scope"),
        new StandardAspectRatio(2.40, "Blu-ray Scope"),
        new StandardAspectRatio(2.55, "Early CinemaScope"),
        new StandardAspectRatio(2.66, "Original 1953 CinemaScope"),
        new StandardAspectRatio(2.76, "Ultra Panavision 70"),
    ];

    /// <summary>
    /// Snaps a measured aspect ratio to the nearest standard ratio, the lower one on a tie.
    /// </summary>
    /// <param name="measuredRatio">The measured width to height ratio.</param>
    /// <returns>The nearest standard ratio, or <c>null</c> when it is further than <see cref="SnapTolerance"/> away.</returns>
    public static StandardAspectRatio? Snap(double measuredRatio)
    {
        StandardAspectRatio? nearest = null;
        double nearestDelta = double.MaxValue;
        foreach (StandardAspectRatio standard in StandardRatios)
        {
            double delta = Math.Abs(measuredRatio - standard.Ratio);
            if (delta >= nearestDelta)
            {
                continue;
            }

            nearest = standard;
            nearestDelta = delta;
        }

        return nearestDelta <= SnapTolerance ? nearest : null;
    }
}
