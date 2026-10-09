using System;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// The trickplay images a measurement was taken from. A stored measurement is current only while its source matches the item's.
/// </summary>
/// <param name="Width">The thumbnail width in pixels, which names the trickplay resolution.</param>
/// <param name="Height">The thumbnail height in pixels.</param>
/// <param name="TileWidth">The number of thumbnails per tile row.</param>
/// <param name="TileHeight">The number of thumbnail rows per tile.</param>
/// <param name="ThumbnailCount">The number of thumbnails.</param>
/// <param name="Interval">The interval between thumbnails in milliseconds.</param>
/// <param name="ItemDateModified">The modification date of the video, which changes when its file is replaced.</param>
public readonly record struct TrickplaySource(
    int Width,
    int Height,
    int TileWidth,
    int TileHeight,
    int ThumbnailCount,
    int Interval,
    DateTime ItemDateModified);
