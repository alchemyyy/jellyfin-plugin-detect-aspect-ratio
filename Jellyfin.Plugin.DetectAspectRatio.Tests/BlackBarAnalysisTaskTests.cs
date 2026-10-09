using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Globalization;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class BlackBarAnalysisTaskTests : IDisposable
{
    private readonly AnalysisFixture fixture = new AnalysisFixture();

    public void Dispose()
    {
        fixture.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_MeasuresEveryVideoWithTrickplayAndPrunesTheRest()
    {
        Movie scope = fixture.AddVideo("Scope Film");
        Movie flat = fixture.AddVideo("Flat Film");
        Movie unscanned = fixture.AddVideo("Unscanned Film");
        fixture.AddTrickplay(scope, 120, 2.39);
        fixture.AddTrickplay(flat, 80, 1.85);

        // Rows of videos that left the library are skipped, and their measurements deleted
        Guid removedItemId = Guid.NewGuid();
        fixture.TrickplayRows.Add(AnalysisFixture.CreateRow(Guid.NewGuid(), 100));
        fixture.Store.Write(removedItemId, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, new TrickplaySource(320, 180, 10, 10, 100, 10000, DateTime.UnixEpoch), 2.39));
        List<double> progress = new List<double>();

        await CreateTask().ExecuteAsync(new SynchronousProgress(progress), TestContext.Current.CancellationToken);

        Assert.Equal(2.39, AspectRatioFunctions.Snap(fixture.Store.Read(scope.Id)!.AspectRatio)?.Ratio);
        Assert.Equal(1.85, AspectRatioFunctions.Snap(fixture.Store.Read(flat.Id)!.AspectRatio)?.Ratio);
        Assert.Null(fixture.Store.Read(unscanned.Id));
        Assert.Null(fixture.Store.Read(removedItemId));
        Assert.Equal(100, progress[^1]);
        Assert.Equal(progress, [.. progress.Order()]);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsTrickplayRowsPageByPage()
    {
        // 230 rows: two full pages and a short one that ends the reading
        for (int index = 0; index < 230; index++)
        {
            fixture.TrickplayRows.Add(AnalysisFixture.CreateRow(Guid.NewGuid(), 100));
        }

        await CreateTask().ExecuteAsync(new SynchronousProgress([]), TestContext.Current.CancellationToken);

        Assert.Equal([0, 100, 200], fixture.RequestedPageOffsets);
    }

    [Fact]
    public void GetDefaultTriggers_IsEmpty()
    {
        // The task runs after Generate Trickplay Images, queued by TrickplayTaskListener
        Assert.Empty(CreateTask().GetDefaultTriggers());
        Assert.Equal("DetectAspectRatioMeasureBlackBars", CreateTask().Key);
        Assert.Equal("Library", CreateTask().Category);
    }

    private BlackBarAnalysisTask CreateTask()
    {
        ILocalizationManager localizationManager = InterfaceFake.Create<ILocalizationManager>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["GetLocalizedString"] = (method, args) => (string?)args[0] == "TasksLibraryCategory" ? "Library" : args[0],
        });
        return new BlackBarAnalysisTask(fixture.LibraryManager, fixture.TrickplayManager, localizationManager, fixture.Analyzer, fixture.Store);
    }

    // Progress<T> reports on the thread pool; the task's reports must be read in order
    private sealed class SynchronousProgress(List<double> reports) : IProgress<double>
    {
        public void Report(double value)
        {
            reports.Add(value);
        }
    }
}
