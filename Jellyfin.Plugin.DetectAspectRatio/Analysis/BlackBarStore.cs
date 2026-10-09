using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DetectAspectRatio.Analysis;

/// <summary>
/// Keeps one measurement per video as a JSON file.
/// </summary>
/// <param name="folderPath">The folder that holds the measurements.</param>
/// <param name="logger">The logger.</param>
public sealed class BlackBarStore(string folderPath, ILogger<BlackBarStore> logger)
{
    /// <summary>
    /// The folder below the server's data folder that holds the measurements.
    /// </summary>
    public const string DirectoryName = "detect-aspect-ratio";

    private const string FileExtension = ".json";
    private const string TemporaryExtension = ".tmp";
    private const string ItemIdFormat = "N";

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions();

    /// <summary>
    /// Reads the measurement of a video.
    /// </summary>
    /// <param name="itemId">The video.</param>
    /// <returns>The stored measurement, or <c>null</c> when there is none or it is unreadable.</returns>
    public BlackBarMeasurement? Read(Guid itemId)
    {
        string path = GetPath(itemId);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<BlackBarMeasurement>(File.ReadAllBytes(path), SerializerOptions) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Recovery: an unreadable measurement is taken again and overwritten
            logger.LogWarning(exception, "Detect Aspect Ratio could not read the measurement {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Stores the measurement of a video, replacing the file at once so a reader never sees a partial one.
    /// </summary>
    /// <param name="itemId">The video.</param>
    /// <param name="measurement">The measurement.</param>
    /// <returns><c>true</c> when the measurement was stored.</returns>
    public bool Write(Guid itemId, BlackBarMeasurement measurement)
    {
        string path = GetPath(itemId);
        string temporaryPath = path + TemporaryExtension;
        try
        {
            Directory.CreateDirectory(folderPath);
            File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(measurement, SerializerOptions));
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Recovery: the video is measured again the next time it is needed
            logger.LogWarning(exception, "Detect Aspect Ratio could not store the measurement {Path}", path);
            return false;
        }
    }

    /// <summary>
    /// Deletes the measurement of a video.
    /// </summary>
    /// <param name="itemId">The video.</param>
    public void Delete(Guid itemId)
    {
        string path = GetPath(itemId);
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Recovery: the next cleanup tries again
            logger.LogWarning(exception, "Detect Aspect Ratio could not delete the measurement {Path}", path);
        }
    }

    /// <summary>
    /// Lists the videos that have a stored measurement.
    /// </summary>
    /// <returns>The video identifiers.</returns>
    public IReadOnlyList<Guid> ListItemIds()
    {
        List<Guid> itemIds = new List<Guid>();
        if (!Directory.Exists(folderPath))
        {
            return itemIds;
        }

        foreach (string path in Directory.EnumerateFiles(folderPath, "*" + FileExtension))
        {
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), ItemIdFormat, out Guid itemId))
            {
                itemIds.Add(itemId);
            }
        }

        return itemIds;
    }

    private string GetPath(Guid itemId)
    {
        return Path.Combine(folderPath, itemId.ToString(ItemIdFormat, CultureInfo.InvariantCulture) + FileExtension);
    }
}
