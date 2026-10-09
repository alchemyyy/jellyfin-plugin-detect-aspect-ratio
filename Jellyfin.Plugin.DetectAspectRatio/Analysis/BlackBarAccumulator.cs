using System;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Collects trickplay thumbnails, then measures the picture inside their black bars. Every thumbnail adds to a per-pixel count
/// of how often that pixel is lit, and the picture is the span of rows and columns lit often enough.
/// </summary>
/// <remarks>
/// Scaling a frame down to a thumbnail blends the line where a bar meets the picture, and its faint pixels still count as lit.
/// Such a line counts as picture, so a measurement rounds the picture outward and errs toward leaving a sliver of bar rather
/// than cutting into the picture.
/// </remarks>
/// <param name="layout">The thumbnail grid.</param>
public sealed class BlackBarAccumulator(TrickplayLayout layout)
{
    /// <summary>
    /// The version of the measuring method. Raise it whenever a change alters results, so stored measurements are taken again.
    /// </summary>
    /// <remarks>
    /// Version 1 trimmed dim edge lines, which cut into pictures whose edge falls inside a thumbnail line.
    /// </remarks>
    public const int MethodVersion = 2;

    /// <summary>
    /// The bytes per pixel of the RGBA tiles the accumulator reads.
    /// </summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// The BT.601 luma above which a pixel is lit, scaled by <see cref="LumaScale"/>. It tolerates JPEG noise in black areas.
    /// </summary>
    public const int LitLumaThreshold = 15 * LumaScale;

    /// <summary>
    /// The fraction of thumbnails in which a pixel must be lit to be picture, so brief changes such as opening logos and IMAX sequences do not count.
    /// </summary>
    public const double PixelQuorumFraction = 0.05;

    /// <summary>
    /// The fraction of its pixels that must be picture for a row or column to be picture.
    /// Whole lines are judged, because JPEG artifacts light single pixels inside thin black bars.
    /// </summary>
    public const double LineFillFraction = 0.25;

    /// <summary>
    /// When the picture exceeds this fraction of the thumbnail in both dimensions, the video has no black bars.
    /// </summary>
    public const double FullFrameFraction = 0.98;

    /// <summary>
    /// The decimals a measured ratio is rounded to.
    /// </summary>
    public const int RatioDecimals = 3;

    private const int LumaScale = 1000;
    private const int RedLumaWeight = 299;
    private const int GreenLumaWeight = 587;
    private const int BlueLumaWeight = 114;

    private readonly int[] litCounts = new int[layout.ThumbnailWidth * layout.ThumbnailHeight];

    /// <summary>
    /// Gets the number of thumbnails added.
    /// </summary>
    public int ThumbnailCount { get; private set; }

    /// <summary>
    /// Adds every thumbnail of one tile.
    /// </summary>
    /// <param name="pixels">The tile in RGBA order, <see cref="BytesPerPixel"/> bytes per pixel.</param>
    /// <param name="tilePixelWidth">The tile width in pixels.</param>
    /// <param name="tilePixelHeight">The tile height in pixels. A shorter tile holds fewer thumbnail rows.</param>
    /// <param name="rowBytes">The bytes per tile row.</param>
    /// <param name="maximumThumbnails">The number of thumbnails left to read, so the empty slots of the last tile are skipped.</param>
    /// <returns>The number of thumbnails read from the tile.</returns>
    public int AddTile(ReadOnlySpan<byte> pixels, int tilePixelWidth, int tilePixelHeight, int rowBytes, int maximumThumbnails)
    {
        int thumbnailWidth = layout.ThumbnailWidth;
        int thumbnailHeight = layout.ThumbnailHeight;
        int thumbnailsRead = 0;
        for (int slotRow = 0; slotRow < layout.TileRows; slotRow++)
        {
            int originY = slotRow * thumbnailHeight;
            if (originY + thumbnailHeight > tilePixelHeight)
            {
                break;
            }

            for (int slotColumn = 0; slotColumn < layout.TileColumns; slotColumn++)
            {
                int originX = slotColumn * thumbnailWidth;
                if (thumbnailsRead >= maximumThumbnails || originX + thumbnailWidth > tilePixelWidth)
                {
                    break;
                }

                for (int row = 0; row < thumbnailHeight; row++)
                {
                    ReadOnlySpan<byte> line = pixels.Slice(((originY + row) * rowBytes) + (originX * BytesPerPixel), thumbnailWidth * BytesPerPixel);
                    Span<int> lineCounts = litCounts.AsSpan(row * thumbnailWidth, thumbnailWidth);
                    for (int column = 0; column < thumbnailWidth; column++)
                    {
                        int offset = column * BytesPerPixel;
                        int luma = (RedLumaWeight * line[offset]) + (GreenLumaWeight * line[offset + 1]) + (BlueLumaWeight * line[offset + 2]);
                        if (luma > LitLumaThreshold)
                        {
                            lineCounts[column]++;
                        }
                    }
                }

                thumbnailsRead++;
            }
        }

        ThumbnailCount += thumbnailsRead;
        return thumbnailsRead;
    }

    /// <summary>
    /// Measures the aspect ratio of the picture inside the black bars of the thumbnails added.
    /// </summary>
    /// <returns>The picture's width to height ratio rounded to <see cref="RatioDecimals"/> decimals, or 0 when it fills the frame or nothing is lit.</returns>
    public double MeasureContentAspectRatio()
    {
        int thumbnailWidth = layout.ThumbnailWidth;
        int thumbnailHeight = layout.ThumbnailHeight;
        int quorum = (int)Math.Ceiling(ThumbnailCount * PixelQuorumFraction);

        int firstRow = -1;
        int lastRow = -1;
        for (int row = 0; row < thumbnailHeight; row++)
        {
            int contentPixels = 0;
            foreach (int count in litCounts.AsSpan(row * thumbnailWidth, thumbnailWidth))
            {
                if (count >= quorum)
                {
                    contentPixels++;
                }
            }

            if ((double)contentPixels / thumbnailWidth < LineFillFraction)
            {
                continue;
            }

            if (firstRow < 0)
            {
                firstRow = row;
            }

            lastRow = row;
        }

        int firstColumn = -1;
        int lastColumn = -1;
        for (int column = 0; column < thumbnailWidth; column++)
        {
            int contentPixels = 0;
            for (int row = 0; row < thumbnailHeight; row++)
            {
                if (litCounts[(row * thumbnailWidth) + column] >= quorum)
                {
                    contentPixels++;
                }
            }

            if ((double)contentPixels / thumbnailHeight < LineFillFraction)
            {
                continue;
            }

            if (firstColumn < 0)
            {
                firstColumn = column;
            }

            lastColumn = column;
        }

        if (firstRow < 0 || firstColumn < 0)
        {
            return 0;
        }

        int contentWidth = lastColumn - firstColumn + 1;
        int contentHeight = lastRow - firstRow + 1;
        if ((double)contentWidth / thumbnailWidth > FullFrameFraction && (double)contentHeight / thumbnailHeight > FullFrameFraction)
        {
            return 0;
        }

        return Math.Round((double)contentWidth / contentHeight, RatioDecimals);
    }
}
