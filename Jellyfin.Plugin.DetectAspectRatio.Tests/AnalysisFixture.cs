using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Trickplay;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

/// <summary>
/// A library with videos, their trickplay rows, and tile folders on disk, behind fakes of Jellyfin's managers.
/// </summary>
internal sealed class AnalysisFixture : IDisposable
{
    public AnalysisFixture()
    {
        Root = Directory.CreateTempSubdirectory("detect-aspect-ratio-analysis-").FullName;
        Store = new BlackBarStore(Path.Combine(Root, BlackBarStore.DirectoryName), NullLogger<BlackBarStore>.Instance);
        LibraryManager = InterfaceFake.Create<ILibraryManager>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["GetLibraryOptions"] = (method, args) => new LibraryOptions { SaveTrickplayWithMedia = SaveTrickplayWithMedia },
            ["GetItemById"] = (method, args) => FindVisibleItem(method, args),
        });
        TrickplayManager = InterfaceFake.Create<ITrickplayManager>(new Dictionary<string, Func<MethodInfo, object?[], object?>>(StringComparer.Ordinal)
        {
            ["GetTrickplayResolutions"] = (method, args) => Task.FromResult(GetResolutions((Guid)args[0]!)),
            ["GetTrickplayDirectory"] = (method, args) => GetTileDirectory((BaseItem)args[0]!, (int)args[1]!, (int)args[2]!, (int)args[3]!, (bool)args[4]!),
            ["GetTrickplayItemsAsync"] = (method, args) => Task.FromResult(GetTrickplayPage((int)args[0]!, (int)args[1]!)),
        });
        Analyzer = new BlackBarAnalyzer(LibraryManager, TrickplayManager, Store, NullLogger<BlackBarAnalyzer>.Instance);
    }

    public string Root { get; }

    public BlackBarStore Store { get; }

    public ILibraryManager LibraryManager { get; }

    public ITrickplayManager TrickplayManager { get; }

    public BlackBarAnalyzer Analyzer { get; }

    public Dictionary<Guid, BaseItem> Items { get; } = new Dictionary<Guid, BaseItem>();

    public List<TrickplayInfo> TrickplayRows { get; } = new List<TrickplayInfo>();

    public List<int> RequestedPageOffsets { get; } = new List<int>();

    public bool SaveTrickplayWithMedia { get; set; }

    /// <summary>
    /// Gets or sets the user the fake library hides every item from.
    /// </summary>
    public User? HiddenFromUser { get; set; }

    public static TrickplayInfo CreateRow(Guid itemId, int thumbnailCount, int width = 320, int height = 180)
    {
        return new TrickplayInfo
        {
            ItemId = itemId,
            Width = width,
            Height = height,
            TileWidth = 10,
            TileHeight = 10,
            ThumbnailCount = thumbnailCount,
            Interval = 10000,
            Bandwidth = 1000,
        };
    }

    public Movie AddVideo(string name)
    {
        Movie video = new Movie
        {
            Id = Guid.NewGuid(),
            Name = name,
            DateModified = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        Items.Add(video.Id, video);
        return video;
    }

    /// <summary>
    /// Adds a trickplay row for a video and writes its tiles where Jellyfin would.
    /// </summary>
    /// <param name="video">The video.</param>
    /// <param name="thumbnailCount">The number of thumbnails.</param>
    /// <param name="contentAspectRatio">The aspect ratio of the picture inside the black bars, or 0 for a full frame.</param>
    /// <param name="withMedia">Whether the tiles are saved beside the media instead of in the metadata folder.</param>
    /// <returns>The row.</returns>
    public TrickplayInfo AddTrickplay(BaseItem video, int thumbnailCount, double contentAspectRatio, bool withMedia = false)
    {
        TrickplayInfo row = CreateRow(video.Id, thumbnailCount);
        TrickplayRows.Add(row);
        TestTiles.WriteVideoTiles(GetTileDirectory(video, row.TileWidth, row.TileHeight, row.Width, withMedia), TestTiles.DefaultLayout, thumbnailCount, contentAspectRatio);
        return row;
    }

    public string GetTileDirectory(BaseItem item, int tileWidth, int tileHeight, int width, bool saveWithMedia)
    {
        string folder = string.Format(CultureInfo.InvariantCulture, "{0} - {1}x{2}", width, tileWidth, tileHeight);
        return Path.Combine(Root, saveWithMedia ? "media" : "metadata", item.Id.ToString("N"), folder);
    }

    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }

    private Dictionary<int, TrickplayInfo> GetResolutions(Guid itemId)
    {
        Dictionary<int, TrickplayInfo> resolutions = new Dictionary<int, TrickplayInfo>();
        foreach (TrickplayInfo row in TrickplayRows)
        {
            if (row.ItemId == itemId)
            {
                resolutions[row.Width] = row;
            }
        }

        return resolutions;
    }

    private IReadOnlyList<TrickplayInfo> GetTrickplayPage(int limit, int offset)
    {
        RequestedPageOffsets.Add(offset);
        List<TrickplayInfo> page = new List<TrickplayInfo>();
        for (int index = offset; index < Math.Min(offset + limit, TrickplayRows.Count); index++)
        {
            page.Add(TrickplayRows[index]);
        }

        return page;
    }

    private object? FindVisibleItem(MethodInfo method, object?[] args)
    {
        if (!Items.TryGetValue((Guid)args[0]!, out BaseItem? item))
        {
            return null;
        }

        // GetItemById(Guid) returns any item; GetItemById<T>(Guid, User) returns a T the user can see
        if (!method.IsGenericMethod)
        {
            return item;
        }

        User? user = args.Length > 1 ? args[1] as User : null;
        bool visible = HiddenFromUser is null || !ReferenceEquals(user, HiddenFromUser);
        return visible && method.GetGenericArguments()[0].IsInstanceOfType(item) ? item : null;
    }
}
