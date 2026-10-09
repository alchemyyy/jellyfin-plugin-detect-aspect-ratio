using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class TrickplayTileFunctionsTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("detect-aspect-ratio-tiles-").FullName;

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void ListTiles_SortsByIndexAndSkipsOtherFiles()
    {
        foreach (string name in (string[])["10.jpg", "2.jpg", "0.jpg", "1.jpg", "cover.jpg", "-1.jpg", "3.png", ".jellyfin-trickplay"])
        {
            File.WriteAllBytes(Path.Combine(directory, name), []);
        }

        IReadOnlyList<string> tiles = TrickplayTileFunctions.ListTiles(directory);

        Assert.Equal(["0.jpg", "1.jpg", "2.jpg", "10.jpg"], tiles.Select(Path.GetFileName));
    }

    [Fact]
    public void ListTiles_MissingFolder_ReturnsNothing()
    {
        Assert.Empty(TrickplayTileFunctions.ListTiles(Path.Combine(directory, "missing")));
    }

    [Theory]
    [InlineData(720, 8, 100, 720)]
    [InlineData(701, 8, 100, 701)]
    [InlineData(800, 8, 100, 800)]
    [InlineData(1, 1, 100, 1)]
    [InlineData(8, 8, 100, 800)]
    [InlineData(700, 8, 100, 800)]
    [InlineData(801, 8, 100, 800)]
    public void ResolveThumbnailCount_TrustsOnlyCountsTheTilesCanHold(int recordedCount, int tileCount, int thumbnailsPerTile, int expected)
    {
        // Rows imported by Jellyfin 10.10 and 10.11 record the tile count, 8 here, instead of the thumbnail count
        Assert.Equal(expected, TrickplayTileFunctions.ResolveThumbnailCount(recordedCount, tileCount, thumbnailsPerTile));
    }

    [Theory]
    [InlineData(2.39)]
    [InlineData(2.35)]
    [InlineData(1.85)]
    [InlineData(4.0 / 3.0)]
    public void MeasureTiles_JpegTiles_SnapToThePictureRatio(double contentAspectRatio)
    {
        int tileCount = TestTiles.WriteVideoTiles(directory, TestTiles.DefaultLayout, 250, contentAspectRatio);

        IReadOnlyList<string> tiles = TrickplayTileFunctions.ListTiles(directory);
        double? measured = TrickplayTileFunctions.MeasureTiles(tiles, TestTiles.DefaultLayout, 250, NullLogger.Instance);

        Assert.Equal(3, tileCount);
        Assert.NotNull(measured);
        Assert.Equal(contentAspectRatio, AspectRatioFunctions.Snap(measured.Value)?.Ratio ?? 0, 0.005);
    }

    [Theory]
    [InlineData("scope-1920x804", 2.353, 2.35)]
    [InlineData("pillarbox-1440x1080", 1.344, 1.33)]
    public void MeasureTiles_TilesJellyfinGenerated_RoundThePictureOutward(string film, double expectedRatio, double expectedCrop)
    {
        // Jellyfin 12.1 made these from 1920 x 1080 test pattern films, whose pictures are 2.388 and 1.333. Its scaler blends each
        // bright bar edge into one line that counts as picture, so the scope film crops to 2.35 and keeps a sliver of bar
        string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", film, "320 - 10x10");

        double? measured = TrickplayTileFunctions.MeasureTiles(TrickplayTileFunctions.ListTiles(fixture), TestTiles.DefaultLayout, 15, NullLogger.Instance);

        Assert.Equal(expectedRatio, measured);
        Assert.Equal(expectedCrop, AspectRatioFunctions.Snap(measured!.Value)?.Ratio);
    }

    [Fact]
    public void MeasureTiles_FullFrameJpegTiles_ReturnsZero()
    {
        TestTiles.WriteVideoTiles(directory, TestTiles.DefaultLayout, 250, 0);

        Assert.Equal(0, TrickplayTileFunctions.MeasureTiles(TrickplayTileFunctions.ListTiles(directory), TestTiles.DefaultLayout, 250, NullLogger.Instance));
    }

    [Fact]
    public void MeasureTiles_UndecodableTile_IsSkipped()
    {
        TestTiles.WriteVideoTiles(directory, TestTiles.DefaultLayout, 200, 2.39);
        File.WriteAllText(Path.Combine(directory, "1.jpg"), "not a JPEG");

        double? measured = TrickplayTileFunctions.MeasureTiles(TrickplayTileFunctions.ListTiles(directory), TestTiles.DefaultLayout, 200, NullLogger.Instance);

        Assert.Equal(2.39, AspectRatioFunctions.Snap(measured!.Value)?.Ratio);
    }

    [Fact]
    public void MeasureTiles_NoReadableTile_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(directory, "0.jpg"), "not a JPEG");

        Assert.Null(TrickplayTileFunctions.MeasureTiles(TrickplayTileFunctions.ListTiles(directory), TestTiles.DefaultLayout, 100, NullLogger.Instance));
        Assert.Null(TrickplayTileFunctions.MeasureTiles([], TestTiles.DefaultLayout, 100, NullLogger.Instance));
    }

    [Fact]
    public void Decode_TruncatedTile_ReturnsNull()
    {
        TestTiles.WriteVideoTiles(directory, TestTiles.DefaultLayout, 100, 2.39);
        string path = Path.Combine(directory, "0.jpg");
        byte[] content = File.ReadAllBytes(path);
        File.WriteAllBytes(path, content[..(content.Length / 2)]);

        // A truncated tile decodes black below the cut, which would read as a bottom bar
        Assert.Null(TrickplayTileFunctions.Decode(path));
    }

    [Fact]
    public void Decode_Tile_ReturnsOpaqueRGBA()
    {
        TestTiles.WriteVideoTiles(directory, TestTiles.DefaultLayout, 100, 2.39);

        using SKBitmap? tile = TrickplayTileFunctions.Decode(Path.Combine(directory, "0.jpg"));

        Assert.NotNull(tile);
        Assert.Equal(SKColorType.Rgba8888, tile.ColorType);
        Assert.Equal((3200, 1800), (tile.Width, tile.Height));
    }
}
