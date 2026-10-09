using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Reads the trickplay tiles of one resolution from disk and measures them with the server's SkiaSharp.
/// </summary>
public static class TrickplayTileFunctions
{
    private const string TileSearchPattern = "*.jpg";

    /// <summary>
    /// Lists the tiles of a trickplay resolution in tile order. Tiles are named by their 0-based index, without padding.
    /// </summary>
    /// <param name="directory">The resolution's tile folder.</param>
    /// <returns>The tile files in numeric order, or an empty list when the folder does not exist.</returns>
    public static IReadOnlyList<string> ListTiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<(int Index, string Path)> tiles = new List<(int Index, string Path)>();
        foreach (string path in Directory.EnumerateFiles(directory, TileSearchPattern))
        {
            if (int.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
            {
                tiles.Add((index, path));
            }
        }

        tiles.Sort((left, right) => left.Index.CompareTo(right.Index));
        return tiles.ConvertAll(tile => tile.Path);
    }

    /// <summary>
    /// Returns the number of thumbnails in a resolution's tiles.
    /// </summary>
    /// <param name="recordedCount">The thumbnail count Jellyfin recorded.</param>
    /// <param name="tileCount">The number of tiles on disk.</param>
    /// <param name="thumbnailsPerTile">The thumbnails one full tile holds.</param>
    /// <returns>
    /// The recorded count when the tiles can hold it, otherwise the capacity of the tiles.
    /// NOTE: Rows that Jellyfin 10.10 and 10.11 imported from existing tiles record the tile count instead.
    /// </returns>
    public static int ResolveThumbnailCount(int recordedCount, int tileCount, int thumbnailsPerTile)
    {
        int capacity = tileCount * thumbnailsPerTile;
        int smallestPossible = capacity - thumbnailsPerTile + 1;
        return recordedCount >= smallestPossible && recordedCount <= capacity ? recordedCount : capacity;
    }

    /// <summary>
    /// Measures the content aspect ratio of every thumbnail in a resolution's tiles.
    /// </summary>
    /// <param name="tilePaths">The tile files in tile order.</param>
    /// <param name="layout">The thumbnail grid.</param>
    /// <param name="thumbnailCount">The number of thumbnails in the tiles, so the empty slots of the last tile are skipped.</param>
    /// <param name="logger">The logger for tiles that cannot be decoded.</param>
    /// <returns>The measured ratio, 0 when the video has no black bars, or <c>null</c> when no tile could be read.</returns>
    public static double? MeasureTiles(IReadOnlyList<string> tilePaths, TrickplayLayout layout, int thumbnailCount, ILogger logger)
    {
        BlackBarAccumulator accumulator = new BlackBarAccumulator(layout);
        foreach (string tilePath in tilePaths)
        {
            if (accumulator.ThumbnailCount >= thumbnailCount)
            {
                break;
            }

            using SKBitmap? tile = Decode(tilePath);
            if (tile is null)
            {
                logger.LogWarning("Detect Aspect Ratio could not decode the trickplay tile {Path}", tilePath);
                continue;
            }

            accumulator.AddTile(tile.GetPixelSpan(), tile.Width, tile.Height, tile.RowBytes, thumbnailCount - accumulator.ThumbnailCount);
        }

        return accumulator.ThumbnailCount == 0 ? null : accumulator.MeasureContentAspectRatio();
    }

    /// <summary>
    /// Decodes a tile into opaque RGBA pixels, the layout <see cref="BlackBarAccumulator.AddTile"/> reads.
    /// </summary>
    /// <param name="path">The tile file.</param>
    /// <returns>The decoded tile, or <c>null</c> when the file is missing, unreadable or truncated.</returns>
    public static SKBitmap? Decode(string path)
    {
        using SKCodec? codec = SKCodec.Create(path);
        if (codec is null)
        {
            return null;
        }

        SKImageInfo info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        SKBitmap tile = new SKBitmap(info);
        if (codec.GetPixels(info, tile.GetPixels()) == SKCodecResult.Success)
        {
            return tile;
        }

        // NOTE: Skia fills the rest of a truncated tile with black, which would read as black bars
        tile.Dispose();
        return null;
    }
}
