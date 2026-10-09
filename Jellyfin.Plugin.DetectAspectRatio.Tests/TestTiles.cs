using System;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using SkiaSharp;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

/// <summary>
/// Builds trickplay tiles the way Jellyfin does: thumbnails row by row on a full grid, black where no thumbnail is drawn.
/// </summary>
public static class TestTiles
{
    public const int JpegQuality = 90;

    /// <summary>
    /// The default grid: 320 x 180 thumbnails, 10 by 10 per tile, Jellyfin's defaults.
    /// </summary>
    public static readonly TrickplayLayout DefaultLayout = new TrickplayLayout(10, 10, 320, 180);

    /// <summary>
    /// Returns the centered content rectangle of a picture with the given aspect ratio inside a thumbnail.
    /// </summary>
    /// <param name="layout">The thumbnail grid.</param>
    /// <param name="contentAspectRatio">The content aspect ratio, or 0 for a full frame.</param>
    /// <returns>The content rectangle within the thumbnail.</returns>
    public static SKRectI GetContentRectangle(TrickplayLayout layout, double contentAspectRatio)
    {
        int width = layout.ThumbnailWidth;
        int height = layout.ThumbnailHeight;
        if (contentAspectRatio <= 0)
        {
            return new SKRectI(0, 0, width, height);
        }

        double frameAspectRatio = (double)width / height;
        if (contentAspectRatio >= frameAspectRatio)
        {
            int contentHeight = (int)Math.Round(width / contentAspectRatio);
            int top = (height - contentHeight) / 2;
            return new SKRectI(0, top, width, top + contentHeight);
        }

        int contentWidth = (int)Math.Round(height * contentAspectRatio);
        int left = (width - contentWidth) / 2;
        return new SKRectI(left, 0, left + contentWidth, height);
    }

    /// <summary>
    /// Draws one tile of thumbnails as opaque RGBA pixels.
    /// </summary>
    /// <param name="layout">The thumbnail grid.</param>
    /// <param name="thumbnailCount">The thumbnails to draw; the remaining slots stay black.</param>
    /// <param name="drawThumbnail">Draws thumbnail n into the canvas, whose origin is the thumbnail's corner.</param>
    /// <returns>The tile.</returns>
    public static SKBitmap DrawTile(TrickplayLayout layout, int thumbnailCount, Action<SKCanvas, int> drawThumbnail)
    {
        SKBitmap tile = new SKBitmap(new SKImageInfo(
            layout.TileColumns * layout.ThumbnailWidth,
            layout.TileRows * layout.ThumbnailHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque));
        using SKCanvas canvas = new SKCanvas(tile);
        canvas.Clear(SKColors.Black);
        for (int thumbnail = 0; thumbnail < thumbnailCount; thumbnail++)
        {
            canvas.Save();
            canvas.Translate((thumbnail % layout.TileColumns) * layout.ThumbnailWidth, (thumbnail / layout.TileColumns) * layout.ThumbnailHeight);
            canvas.ClipRect(new SKRect(0, 0, layout.ThumbnailWidth, layout.ThumbnailHeight));
            drawThumbnail(canvas, thumbnail);
            canvas.Restore();
        }

        canvas.Flush();
        return tile;
    }

    /// <summary>
    /// Draws a textured picture into a content rectangle, varied per thumbnail like real footage.
    /// </summary>
    /// <param name="canvas">The thumbnail canvas.</param>
    /// <param name="content">The content rectangle.</param>
    /// <param name="thumbnail">The thumbnail number, which varies the picture.</param>
    public static void DrawPicture(SKCanvas canvas, SKRectI content, int thumbnail)
    {
        byte baseLevel = (byte)(70 + ((thumbnail * 37) % 120));
        using SKPaint fill = new SKPaint { Color = new SKColor(baseLevel, (byte)(baseLevel - 20), (byte)(baseLevel + 15)) };
        canvas.DrawRect(content, fill);
        using SKPaint stripe = new SKPaint { Color = new SKColor(230, 220, 200) };
        for (int x = content.Left + (thumbnail % 7); x < content.Right; x += 23)
        {
            canvas.DrawRect(new SKRect(x, content.Top, Math.Min(x + 4, content.Right), content.Bottom), stripe);
        }
    }

    /// <summary>
    /// Writes the tiles of a video whose picture has the given aspect ratio, named like Jellyfin's tiles.
    /// </summary>
    /// <param name="directory">The resolution's tile folder, created when missing.</param>
    /// <param name="layout">The thumbnail grid.</param>
    /// <param name="thumbnailCount">The number of thumbnails.</param>
    /// <param name="contentAspectRatio">The aspect ratio of the picture inside the black bars, or 0 for a full frame.</param>
    /// <returns>The number of tiles written.</returns>
    public static int WriteVideoTiles(string directory, TrickplayLayout layout, int thumbnailCount, double contentAspectRatio)
    {
        Directory.CreateDirectory(directory);
        SKRectI content = GetContentRectangle(layout, contentAspectRatio);
        int thumbnailsPerTile = layout.TileColumns * layout.TileRows;
        int tileCount = (thumbnailCount + thumbnailsPerTile - 1) / thumbnailsPerTile;
        for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            int firstThumbnail = tileIndex * thumbnailsPerTile;
            int tileThumbnails = Math.Min(thumbnailsPerTile, thumbnailCount - firstThumbnail);
            using SKBitmap tile = DrawTile(layout, tileThumbnails, (canvas, thumbnail) => DrawPicture(canvas, content, firstThumbnail + thumbnail));
            WriteJpeg(tile, Path.Combine(directory, tileIndex.ToString(CultureInfo.InvariantCulture) + ".jpg"));
        }

        return tileCount;
    }

    /// <summary>
    /// Encodes a tile as JPEG the way Jellyfin saves it.
    /// </summary>
    /// <param name="tile">The tile.</param>
    /// <param name="path">The file to write.</param>
    public static void WriteJpeg(SKBitmap tile, string path)
    {
        using FileStream stream = File.Create(path);
        if (!tile.Encode(stream, SKEncodedImageFormat.Jpeg, JpegQuality))
        {
            throw new InvalidOperationException("SkiaSharp could not encode " + path);
        }
    }
}
