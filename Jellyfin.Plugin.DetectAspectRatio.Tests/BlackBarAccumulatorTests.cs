using System;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class BlackBarAccumulatorTests
{
    private static readonly TrickplayLayout Layout = TestTiles.DefaultLayout;

    [Theory]
    [InlineData(2.39, 320, 134)]
    [InlineData(2.35, 320, 136)]
    [InlineData(1.85, 320, 173)]
    [InlineData(4.0 / 3.0, 240, 180)]
    [InlineData(1.0, 180, 180)]
    public void Measure_PictureInsideBlackBars_ReturnsThePictureRatio(double contentAspectRatio, int contentWidth, int contentHeight)
    {
        SKRectI content = TestTiles.GetContentRectangle(Layout, contentAspectRatio);
        Assert.Equal((contentWidth, contentHeight), (content.Width, content.Height));

        double measured = MeasureThumbnails(100, (canvas, thumbnail) => TestTiles.DrawPicture(canvas, content, thumbnail));

        Assert.Equal(Math.Round((double)contentWidth / contentHeight, BlackBarAccumulator.RatioDecimals), measured);
    }

    [Fact]
    public void Measure_FullFramePicture_ReturnsZero()
    {
        SKRectI content = TestTiles.GetContentRectangle(Layout, 0);

        Assert.Equal(0, MeasureThumbnails(100, (canvas, thumbnail) => TestTiles.DrawPicture(canvas, content, thumbnail)));
    }

    [Fact]
    public void Measure_BarsUnderTwoPercentOfTheFrame_ReturnsZero()
    {
        // 177 of 180 rows is 98.3 percent, which counts as the full frame
        SKRectI content = new SKRectI(0, 1, 320, 178);

        Assert.Equal(0, MeasureThumbnails(100, (canvas, thumbnail) => TestTiles.DrawPicture(canvas, content, thumbnail)));
    }

    [Fact]
    public void Measure_AllBlack_ReturnsZero()
    {
        Assert.Equal(0, MeasureThumbnails(100, (canvas, thumbnail) => { }));
    }

    [Fact]
    public void Measure_BriefFullFrameShots_DoNotHideTheBars()
    {
        // Four of 100 thumbnails (an opening logo, an IMAX shot) fill the frame, below the 5 percent quorum
        SKRectI letterbox = TestTiles.GetContentRectangle(Layout, 2.39);
        SKRectI fullFrame = TestTiles.GetContentRectangle(Layout, 0);

        double measured = MeasureThumbnails(100, (canvas, thumbnail) => TestTiles.DrawPicture(canvas, thumbnail < 4 ? fullFrame : letterbox, thumbnail));

        Assert.Equal(Math.Round(320.0 / 134, BlackBarAccumulator.RatioDecimals), measured);
    }

    [Fact]
    public void Measure_FullFrameShotsAtTheQuorum_CountAsPicture()
    {
        SKRectI letterbox = TestTiles.GetContentRectangle(Layout, 2.39);
        SKRectI fullFrame = TestTiles.GetContentRectangle(Layout, 0);

        double measured = MeasureThumbnails(100, (canvas, thumbnail) => TestTiles.DrawPicture(canvas, thumbnail < 5 ? fullFrame : letterbox, thumbnail));

        Assert.Equal(0, measured);
    }

    [Fact]
    public void Measure_ScatteredLitPixelsInsideTheBars_AreIgnored()
    {
        // JPEG artifacts light single pixels inside thin bars; a bar row stays black as long as under a quarter of it is lit
        SKRectI content = TestTiles.GetContentRectangle(Layout, 1.85);
        using SKPaint speck = new SKPaint { Color = SKColors.White };

        double measured = MeasureThumbnails(100, (canvas, thumbnail) =>
        {
            TestTiles.DrawPicture(canvas, content, thumbnail);
            for (int x = 0; x < 320; x += 5)
            {
                canvas.DrawRect(new SKRect(x, 0, x + 1, content.Top), speck);
                canvas.DrawRect(new SKRect(x, content.Bottom, x + 1, 180), speck);
            }
        });

        Assert.Equal(Math.Round(320.0 / 173, BlackBarAccumulator.RatioDecimals), measured);
    }

    [Fact]
    public void Measure_DimPixelsAtTheLumaThreshold_StayBlack()
    {
        // Luma 15 is the brightest black; JPEG noise in black bars stays below it
        SKRectI content = TestTiles.GetContentRectangle(Layout, 2.39);
        using SKPaint dim = new SKPaint { Color = new SKColor(15, 15, 15) };

        double measured = MeasureThumbnails(100, (canvas, thumbnail) =>
        {
            canvas.DrawRect(new SKRect(0, 0, 320, 180), dim);
            TestTiles.DrawPicture(canvas, content, thumbnail);
        });

        Assert.Equal(Math.Round(320.0 / 134, BlackBarAccumulator.RatioDecimals), measured);
    }

    [Theory]
    [InlineData(0.22)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void Measure_BlendedEdgeLines_CountAsPicture(double blendBrightness)
    {
        // Scaling a 1920 x 1080 frame down to 320 x 180 blends the line where a bar meets the picture; counting it rounds the
        // picture outward, so the crop leaves a sliver of bar instead of cutting the picture
        SKRectI content = TestTiles.GetContentRectangle(Layout, 2.39);

        double measured = MeasureThumbnails(100, (canvas, thumbnail) =>
        {
            TestTiles.DrawPicture(canvas, content, thumbnail);
            DrawBlendedLine(canvas, new SKRectI(0, content.Top - 1, 320, content.Top), thumbnail, blendBrightness);
            DrawBlendedLine(canvas, new SKRectI(0, content.Bottom, 320, content.Bottom + 1), thumbnail, blendBrightness);
        });

        Assert.Equal(Math.Round(320.0 / 136, BlackBarAccumulator.RatioDecimals), measured);
    }

    [Fact]
    public void Measure_BlendedEdgeColumns_CountAsPicture()
    {
        SKRectI content = TestTiles.GetContentRectangle(Layout, 4.0 / 3.0);

        double measured = MeasureThumbnails(100, (canvas, thumbnail) =>
        {
            TestTiles.DrawPicture(canvas, content, thumbnail);
            DrawBlendedLine(canvas, new SKRectI(content.Left - 1, 0, content.Left, 180), thumbnail, 0.3);
            DrawBlendedLine(canvas, new SKRectI(content.Right, 0, content.Right + 1, 180), thumbnail, 0.3);
        });

        Assert.Equal(Math.Round(242.0 / 180, BlackBarAccumulator.RatioDecimals), measured);
    }

    [Fact]
    public void Measure_FaintEdgeLines_StayBar()
    {
        // A blend too dark to light its pixels in enough thumbnails is bar
        SKRectI content = TestTiles.GetContentRectangle(Layout, 2.39);

        double measured = MeasureThumbnails(100, (canvas, thumbnail) =>
        {
            TestTiles.DrawPicture(canvas, content, thumbnail);
            DrawBlendedLine(canvas, new SKRectI(0, content.Top - 1, 320, content.Top), thumbnail, 0.05);
            DrawBlendedLine(canvas, new SKRectI(0, content.Bottom, 320, content.Bottom + 1), thumbnail, 0.05);
        });

        Assert.Equal(Math.Round(320.0 / 134, BlackBarAccumulator.RatioDecimals), measured);
    }

    [Fact]
    public void AddTile_StopsAtTheThumbnailCount()
    {
        // A last tile holds 37 thumbnails; its other slots are black and must not count as thumbnails
        SKRectI content = TestTiles.GetContentRectangle(Layout, 0);
        using SKBitmap tile = TestTiles.DrawTile(Layout, 37, (canvas, thumbnail) => TestTiles.DrawPicture(canvas, content, thumbnail));
        BlackBarAccumulator accumulator = new BlackBarAccumulator(Layout);

        int read = accumulator.AddTile(tile.GetPixelSpan(), tile.Width, tile.Height, tile.RowBytes, 37);

        Assert.Equal(37, read);
        Assert.Equal(37, accumulator.ThumbnailCount);
        Assert.Equal(0, accumulator.MeasureContentAspectRatio());
    }

    [Fact]
    public void AddTile_ShortTile_ReadsOnlyItsCompleteRows()
    {
        // A tile cut after two and a half thumbnail rows holds 20 complete thumbnails
        using SKBitmap tile = TestTiles.DrawTile(Layout, 100, (canvas, thumbnail) => canvas.Clear(SKColors.White));
        int shortHeight = (Layout.ThumbnailHeight * 5) / 2;
        BlackBarAccumulator accumulator = new BlackBarAccumulator(Layout);

        int read = accumulator.AddTile(tile.GetPixelSpan()[..(tile.RowBytes * shortHeight)], tile.Width, shortHeight, tile.RowBytes, 100);

        Assert.Equal(20, read);
    }

    [Fact]
    public void AddTile_RowsWithPadding_UsesTheRowStride()
    {
        const int Padding = 64;
        TrickplayLayout layout = new TrickplayLayout(2, 1, 4, 4);
        int rowBytes = (layout.TileColumns * layout.ThumbnailWidth * BlackBarAccumulator.BytesPerPixel) + Padding;
        byte[] pixels = new byte[rowBytes * layout.ThumbnailHeight];

        // Light rows 1 and 2 of the second thumbnail, and fill the padding with white that must be ignored
        for (int row = 1; row <= 2; row++)
        {
            for (int column = 0; column < layout.ThumbnailWidth; column++)
            {
                SetPixel(pixels, rowBytes, layout.ThumbnailWidth + column, row, 200);
            }
        }

        for (int row = 0; row < layout.ThumbnailHeight; row++)
        {
            pixels.AsSpan((row * rowBytes) + (rowBytes - Padding), Padding).Fill(255);
        }

        BlackBarAccumulator accumulator = new BlackBarAccumulator(layout);
        int read = accumulator.AddTile(pixels, layout.TileColumns * layout.ThumbnailWidth, layout.ThumbnailHeight, rowBytes, 2);

        // Lit in one of two thumbnails, rows 1 and 2 are a 4 x 2 picture inside 4 x 4 frames
        Assert.Equal(2, read);
        Assert.Equal(2.0, accumulator.MeasureContentAspectRatio());
    }

    [Theory]
    [InlineData(15, 15, 15, 0)]
    [InlineData(16, 16, 16, 1)]
    [InlineData(0, 25, 0, 0)]
    [InlineData(0, 26, 0, 1)]
    [InlineData(50, 0, 0, 0)]
    [InlineData(51, 0, 0, 1)]
    [InlineData(0, 0, 131, 0)]
    [InlineData(0, 0, 132, 1)]
    public void AddTile_LitIsBT601LumaAboveFifteen(byte red, byte green, byte blue, double expectedRatio)
    {
        // A lit pixel beside a black one is a 1 x 1 picture inside a 2 x 1 frame, which measures 1; an unlit one leaves no picture, which measures 0
        TrickplayLayout layout = new TrickplayLayout(1, 1, 2, 1);
        byte[] pixels = [red, green, blue, 255, 0, 0, 0, 255];
        BlackBarAccumulator accumulator = new BlackBarAccumulator(layout);

        accumulator.AddTile(pixels, 2, 1, 2 * BlackBarAccumulator.BytesPerPixel, 1);

        Assert.Equal(expectedRatio, accumulator.MeasureContentAspectRatio());
    }

    private static double MeasureThumbnails(int thumbnailCount, Action<SKCanvas, int> drawThumbnail)
    {
        using SKBitmap tile = TestTiles.DrawTile(Layout, thumbnailCount, drawThumbnail);
        BlackBarAccumulator accumulator = new BlackBarAccumulator(Layout);
        int read = accumulator.AddTile(tile.GetPixelSpan(), tile.Width, tile.Height, tile.RowBytes, thumbnailCount);
        Assert.Equal(thumbnailCount, read);
        return accumulator.MeasureContentAspectRatio();
    }

    // Draws a line that blends bar and picture: the thumbnail's picture color scaled down, so most of its pixels stay above the lit threshold
    private static void DrawBlendedLine(SKCanvas canvas, SKRectI line, int thumbnail, double brightness)
    {
        byte baseLevel = (byte)(70 + ((thumbnail * 37) % 120));
        using SKPaint fill = new SKPaint { Color = new SKColor((byte)(baseLevel * brightness), (byte)((baseLevel - 20) * brightness), (byte)((baseLevel + 15) * brightness)) };
        canvas.DrawRect(line, fill);
    }

    private static void SetPixel(byte[] pixels, int rowBytes, int x, int y, byte level)
    {
        int offset = (y * rowBytes) + (x * BlackBarAccumulator.BytesPerPixel);
        pixels[offset] = level;
        pixels[offset + 1] = level;
        pixels[offset + 2] = level;
        pixels[offset + 3] = 255;
    }
}
