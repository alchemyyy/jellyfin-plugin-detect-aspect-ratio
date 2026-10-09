using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Measures every video that has trickplay images, so playback never waits for a measurement, and deletes the measurements of videos that no longer have them.
/// It runs daily, once on a server where it never ran (<see cref="FirstRunQueuer"/>), and after each Generate Trickplay Images run (<see cref="TrickplayTaskListener"/>).
/// </summary>
/// <param name="libraryManager">The library manager.</param>
/// <param name="trickplayManager">The trickplay manager.</param>
/// <param name="localizationManager">The localization manager, for the category name.</param>
/// <param name="analyzer">The analyzer.</param>
/// <param name="store">The measurement store.</param>
public sealed class BlackBarAnalysisTask(
    ILibraryManager libraryManager,
    ITrickplayManager trickplayManager,
    ILocalizationManager localizationManager,
    BlackBarAnalyzer analyzer,
    BlackBarStore store) : IScheduledTask
{
    /// <summary>
    /// The task key.
    /// </summary>
    public const string TaskKey = "DetectAspectRatioComputeAspectRatios";

    private const int PageSize = 100;
    private const string LibraryCategoryKey = "TasksLibraryCategory";

    // An hour after Generate Trickplay Images, which Jellyfin runs daily at 3 AM by default
    private static readonly TimeSpan DailyRunTime = TimeSpan.FromHours(4);

    /// <inheritdoc />
    public string Name => "Compute Aspect Ratios from Trickplay Data";

    /// <inheritdoc />
    public string Key => TaskKey;

    /// <inheritdoc />
    public string Description => "Computes the aspect ratio inside the black bars of each video from its trickplay images, so playback can crop them at once.";

    /// <inheritdoc />
    public string Category => localizationManager.GetLocalizedString(LibraryCategoryKey);

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // One row per video and resolution; only videos with trickplay images can be measured
        HashSet<Guid> itemIds = new HashSet<Guid>();
        int offset = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<TrickplayInfo> page = await trickplayManager.GetTrickplayItemsAsync(PageSize, offset).ConfigureAwait(false);
            foreach (TrickplayInfo resolution in page)
            {
                itemIds.Add(resolution.ItemId);
            }

            if (page.Count < PageSize)
            {
                break;
            }

            offset += page.Count;
        }

        int visitedCount = 0;
        foreach (Guid itemId in itemIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (libraryManager.GetItemById(itemId) is Video video)
            {
                await analyzer.GetMeasurementAsync(video, cancellationToken).ConfigureAwait(false);
            }

            visitedCount++;
            progress.Report(100.0 * visitedCount / itemIds.Count);
        }

        // A measurement is only valid with the trickplay images it was taken from
        foreach (Guid itemId in store.ListItemIds())
        {
            if (!itemIds.Contains(itemId))
            {
                store.Delete(itemId);
            }
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Library scans can extract trickplay images too, and TrickplayTaskListener never hears of those
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = DailyRunTime.Ticks,
            },
        ];
    }
}
