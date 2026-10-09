using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class BlackBarStoreTests : IDisposable
{
    private static readonly TrickplaySource Source = new TrickplaySource(320, 180, 10, 10, 720, 10000, new DateTime(2026, 10, 8, 12, 30, 15, DateTimeKind.Utc));

    private readonly string directory = Path.Combine(Directory.CreateTempSubdirectory("detect-aspect-ratio-store-").FullName, BlackBarStore.DirectoryName);

    public void Dispose()
    {
        Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
    }

    [Fact]
    public void Write_ThenRead_ReturnsAnEqualMeasurement()
    {
        BlackBarStore store = CreateStore();
        Guid itemId = Guid.NewGuid();
        BlackBarMeasurement measurement = new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 2.388);

        Assert.True(store.Write(itemId, measurement));

        Assert.Equal(measurement, store.Read(itemId));
        Assert.Equal(Source, store.Read(itemId)!.Source);
        Assert.Equal([itemId.ToString("N") + ".json"], Directory.GetFiles(directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Write_ReplacesTheEarlierMeasurement()
    {
        BlackBarStore store = CreateStore();
        Guid itemId = Guid.NewGuid();
        store.Write(itemId, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 2.388));

        store.Write(itemId, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source with { ThumbnailCount = 721 }, 0));

        Assert.Equal(new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source with { ThumbnailCount = 721 }, 0), store.Read(itemId));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void Read_Unmeasured_ReturnsNull()
    {
        Assert.Null(CreateStore().Read(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"Source\":\"x\",\"AspectRatio\":1}")]
    public void Read_UnreadableFile_ReturnsNull(string content)
    {
        Guid itemId = Guid.NewGuid();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, itemId.ToString("N") + ".json"), content);

        Assert.Null(CreateStore().Read(itemId));
    }

    [Fact]
    public void ListItemIds_ListsOnlyMeasurements()
    {
        BlackBarStore store = CreateStore();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        store.Write(first, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 0));
        store.Write(second, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 1.85));
        File.WriteAllText(Path.Combine(directory, "notes.json"), "{}");
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json.tmp"), "{}");

        Assert.Equal(new[] { first, second }.Order(), store.ListItemIds().Order());
    }

    [Fact]
    public void ListItemIds_BeforeTheFirstWrite_ReturnsNothing()
    {
        Assert.Empty(CreateStore().ListItemIds());
    }

    [Fact]
    public void Delete_RemovesTheMeasurement()
    {
        BlackBarStore store = CreateStore();
        Guid itemId = Guid.NewGuid();
        store.Write(itemId, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 2.388));

        store.Delete(itemId);
        store.Delete(Guid.NewGuid());

        Assert.Null(store.Read(itemId));
        Assert.Empty(store.ListItemIds());
    }

    private BlackBarStore CreateStore()
    {
        return new BlackBarStore(directory, NullLogger<BlackBarStore>.Instance);
    }
}
