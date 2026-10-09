using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Measures the black bars of videos from their trickplay tiles, and keeps each measurement until the trickplay images change.
/// </summary>
/// <param name="libraryManager">The library manager.</param>
/// <param name="trickplayManager">The trickplay manager.</param>
/// <param name="store">The measurement store.</param>
/// <param name="logger">The logger.</param>
public sealed class BlackBarAnalyzer(
    ILibraryManager libraryManager,
    ITrickplayManager trickplayManager,
    BlackBarStore store,
    ILogger<BlackBarAnalyzer> logger)
{
    // Measuring is CPU-bound, so the scheduled task and playback requests take turns
    private readonly Lock measureLock = new Lock();

    /// <summary>
    /// Gets the measurement of a video, measuring it first when its trickplay images were never measured or changed since.
    /// </summary>
    /// <param name="video">The video.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The measurement, or <c>null</c> when the video has no readable trickplay images.</returns>
    public async Task<BlackBarMeasurement?> GetMeasurementAsync(Video video, CancellationToken cancellationToken)
    {
        Dictionary<int, TrickplayInfo> resolutions = await trickplayManager.GetTrickplayResolutions(video.Id).ConfigureAwait(false);
        TrickplayInfo? resolution = SelectResolution(resolutions.Values);
        return resolution is null ? null : MeasureIfStale(video, resolution, cancellationToken);
    }

    /// <summary>
    /// Picks the trickplay resolution to measure: the smallest complete one, since black bars need no detail and small tiles decode fastest.
    /// </summary>
    /// <param name="resolutions">The video's trickplay resolutions.</param>
    /// <returns>The resolution, or <c>null</c> when none is complete.</returns>
    public static TrickplayInfo? SelectResolution(IEnumerable<TrickplayInfo> resolutions)
    {
        TrickplayInfo? selected = null;
        foreach (TrickplayInfo resolution in resolutions)
        {
            // Jellyfin records the thumbnail height after generating the tiles
            if (resolution.Width <= 0 || resolution.Height <= 0 || resolution.TileWidth <= 0 || resolution.TileHeight <= 0 || resolution.ThumbnailCount <= 0)
            {
                continue;
            }

            if (selected is null || resolution.Width < selected.Width)
            {
                selected = resolution;
            }
        }

        return selected;
    }

    private BlackBarMeasurement? MeasureIfStale(Video video, TrickplayInfo resolution, CancellationToken cancellationToken)
    {
        TrickplaySource source = new TrickplaySource(
            resolution.Width,
            resolution.Height,
            resolution.TileWidth,
            resolution.TileHeight,
            resolution.ThumbnailCount,
            resolution.Interval,
            video.DateModified);
        BlackBarMeasurement? measurement = store.Read(video.Id);
        if (measurement?.IsCurrent(source) == true)
        {
            return measurement;
        }

        lock (measureLock)
        {
            // Another caller may have measured the video while this one waited
            measurement = store.Read(video.Id);
            if (measurement?.IsCurrent(source) == true)
            {
                return measurement;
            }

            cancellationToken.ThrowIfCancellationRequested();
            double? aspectRatio = Measure(video, resolution);
            if (aspectRatio is null)
            {
                return null;
            }

            measurement = new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, source, aspectRatio.Value);
            store.Write(video.Id, measurement);
            return measurement;
        }
    }

    private double? Measure(Video video, TrickplayInfo resolution)
    {
        try
        {
            string? directory = FindTileDirectory(video, resolution);
            if (directory is null)
            {
                logger.LogWarning("Detect Aspect Ratio found no trickplay tiles of {ItemName} at {Width} px", video.Name, resolution.Width);
                return null;
            }

            IReadOnlyList<string> tilePaths = TrickplayTileFunctions.ListTiles(directory);
            int thumbnailCount = TrickplayTileFunctions.ResolveThumbnailCount(resolution.ThumbnailCount, tilePaths.Count, resolution.TileWidth * resolution.TileHeight);
            TrickplayLayout layout = new TrickplayLayout(resolution.TileWidth, resolution.TileHeight, resolution.Width, resolution.Height);
            long startTimestamp = Stopwatch.GetTimestamp();
            double? aspectRatio = TrickplayTileFunctions.MeasureTiles(tilePaths, layout, thumbnailCount, logger);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            switch (aspectRatio)
            {
                case null:
                    logger.LogWarning("Detect Aspect Ratio could not read any trickplay tile of {ItemName} in {Directory}", video.Name, directory);
                    break;
                case 0:
                    logger.LogInformation("Detect Aspect Ratio found no black bars in {ItemName} ({Milliseconds:F0} ms)", video.Name, elapsed.TotalMilliseconds);
                    break;
                default:
                    logger.LogInformation("Detect Aspect Ratio measured {ItemName} at {AspectRatio:F3}:1 inside its black bars ({Milliseconds:F0} ms)", video.Name, aspectRatio, elapsed.TotalMilliseconds);
                    break;
            }

            return aspectRatio;
        }
        catch (Exception exception)
        {
            // Recovery: the video is measured again the next time it is needed; SkiaSharp without its native library lands here
            logger.LogError(exception, "Detect Aspect Ratio could not measure the black bars of {ItemName}", video.Name);
            return null;
        }
    }

    private string? FindTileDirectory(Video video, TrickplayInfo resolution)
    {
        // The library setting decides where Jellyfin saves tiles, but existing tiles stay where they are until Jellyfin moves them
        bool saveWithMedia = libraryManager.GetLibraryOptions(video).SaveTrickplayWithMedia;
        string directory = GetDirectory(saveWithMedia);
        if (Directory.Exists(directory))
        {
            return directory;
        }

        directory = GetDirectory(!saveWithMedia);
        return Directory.Exists(directory) ? directory : null;

        string GetDirectory(bool withMedia)
        {
            return trickplayManager.GetTrickplayDirectory(video, resolution.TileWidth, resolution.TileHeight, resolution.Width, withMedia);
        }
    }
}
