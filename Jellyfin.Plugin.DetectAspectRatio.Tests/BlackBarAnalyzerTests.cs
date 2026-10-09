using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class BlackBarAnalyzerTests : IDisposable
{
    private readonly AnalysisFixture fixture = new AnalysisFixture();

    public void Dispose()
    {
        fixture.Dispose();
    }

    [Fact]
    public async Task GetMeasurementAsync_MeasuresOnceAndKeepsTheMeasurement()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Scope Film");
        TrickplayInfo row = fixture.AddTrickplay(video, 250, 2.39);

        BlackBarMeasurement? first = await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken);

        Assert.NotNull(first);
        Assert.Equal(2.39, AspectRatioFunctions.Snap(first.AspectRatio)?.Ratio);
        Assert.Equal(new TrickplaySource(320, 180, 10, 10, 250, 10000, video.DateModified), first.Source);
        Assert.Equal(first, fixture.Store.Read(video.Id));

        // Without its tiles the video could not be measured again, so the second answer is the stored one
        Directory.Delete(fixture.GetTileDirectory(video, row.TileWidth, row.TileHeight, row.Width, false), recursive: true);
        Assert.Equal(first, await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken));
    }

    [Fact]
    public async Task GetMeasurementAsync_RegeneratedTrickplay_MeasuresAgain()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Recut Film");
        fixture.AddTrickplay(video, 250, 2.39);
        await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken);

        fixture.TrickplayRows.Clear();
        fixture.AddTrickplay(video, 260, 0);
        BlackBarMeasurement? measurement = await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken);

        Assert.Equal(0, measurement?.AspectRatio);
        Assert.Equal(260, measurement?.Source.ThumbnailCount);
    }

    [Fact]
    public async Task GetMeasurementAsync_ReplacedFile_MeasuresAgain()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Replaced Film");
        TrickplayInfo row = fixture.AddTrickplay(video, 250, 2.39);
        await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken);

        // Trickplay images regenerated with the same settings for a new file only differ in the video's date
        TestTiles.WriteVideoTiles(fixture.GetTileDirectory(video, row.TileWidth, row.TileHeight, row.Width, false), TestTiles.DefaultLayout, 250, 1.85);
        video.DateModified = video.DateModified.AddDays(1);
        BlackBarMeasurement? measurement = await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken);

        Assert.Equal(1.85, AspectRatioFunctions.Snap(measurement!.AspectRatio)?.Ratio);
    }

    [Fact]
    public async Task GetMeasurementAsync_OlderMethod_MeasuresAgain()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Remeasured Film");
        fixture.AddTrickplay(video, 250, 2.39);
        BlackBarMeasurement current = (await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken))!;

        // A plugin update that improves the method takes every measurement again
        fixture.Store.Write(video.Id, current with { MethodVersion = BlackBarAccumulator.MethodVersion - 1, AspectRatio = 2.353 });

        Assert.Equal(current, await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken));
    }

    [Fact]
    public async Task GetMeasurementAsync_TilesBesideTheMedia_AreFoundWhereverTheSettingPoints()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Movie video = fixture.AddVideo("Moved Film");
        fixture.AddTrickplay(video, 250, 2.39, withMedia: true);
        fixture.SaveTrickplayWithMedia = false;

        BlackBarMeasurement? measurement = await fixture.Analyzer.GetMeasurementAsync(video, cancellationToken);

        Assert.Equal(2.39, AspectRatioFunctions.Snap(measurement!.AspectRatio)?.Ratio);
    }

    [Fact]
    public async Task GetMeasurementAsync_WithoutTrickplay_ReturnsNull()
    {
        Movie video = fixture.AddVideo("Unscanned Film");

        Assert.Null(await fixture.Analyzer.GetMeasurementAsync(video, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Store.ListItemIds());
    }

    [Fact]
    public async Task GetMeasurementAsync_MissingTiles_ReturnsNullAndStoresNothing()
    {
        Movie video = fixture.AddVideo("Deleted Tiles");
        fixture.TrickplayRows.Add(AnalysisFixture.CreateRow(video.Id, 250));

        Assert.Null(await fixture.Analyzer.GetMeasurementAsync(video, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Store.ListItemIds());
    }

    [Fact]
    public async Task GetMeasurementAsync_Cancelled_Throws()
    {
        Movie video = fixture.AddVideo("Cancelled Film");
        fixture.AddTrickplay(video, 100, 2.39);
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Analyzer.GetMeasurementAsync(video, cancellation.Token));
        Assert.Empty(fixture.Store.ListItemIds());
    }

    [Fact]
    public void SelectResolution_PicksTheSmallestCompleteResolution()
    {
        Guid itemId = Guid.NewGuid();
        TrickplayInfo large = AnalysisFixture.CreateRow(itemId, 250, 640, 360);
        TrickplayInfo medium = AnalysisFixture.CreateRow(itemId, 250, 480, 270);

        // Jellyfin records the height only once the tiles are written
        TrickplayInfo unfinished = AnalysisFixture.CreateRow(itemId, 250, 320, 0);

        Assert.Same(medium, BlackBarAnalyzer.SelectResolution([large, unfinished, medium]));
        Assert.Null(BlackBarAnalyzer.SelectResolution([unfinished]));
        Assert.Null(BlackBarAnalyzer.SelectResolution([]));
    }
}
