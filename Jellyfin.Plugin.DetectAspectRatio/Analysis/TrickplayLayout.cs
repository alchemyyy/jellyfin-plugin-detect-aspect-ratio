namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// The thumbnail grid of one trickplay resolution. Tiles are numbered JPEG files that hold thumbnails row by row.
/// </summary>
/// <param name="TileColumns">The number of thumbnails per tile row.</param>
/// <param name="TileRows">The number of thumbnail rows per tile.</param>
/// <param name="ThumbnailWidth">The width of one thumbnail in pixels.</param>
/// <param name="ThumbnailHeight">The height of one thumbnail in pixels.</param>
public readonly record struct TrickplayLayout(int TileColumns, int TileRows, int ThumbnailWidth, int ThumbnailHeight);
