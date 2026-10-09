using System;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using Jellyfin.Plugin.DetectAspectRatio.Api;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class BlackBarInfoDtoTests
{
    private static readonly Guid ItemId = Guid.NewGuid();
    private static readonly TrickplaySource Source = new TrickplaySource(320, 180, 10, 10, 720, 10000, DateTime.UnixEpoch);

    [Fact]
    public void Create_Unmeasured_IsNotMeasured()
    {
        Assert.Equal(new BlackBarInfoDto(ItemId, false, 0, 0, null), BlackBarInfoDto.Create(ItemId, null));
    }

    [Fact]
    public void Create_NoBlackBars_IsMeasuredWithoutACropRatio()
    {
        Assert.Equal(new BlackBarInfoDto(ItemId, true, 0, 0, null), BlackBarInfoDto.Create(ItemId, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 0)));
    }

    [Fact]
    public void Create_BlackBars_SnapsToTheStandardRatio()
    {
        Assert.Equal(
            new BlackBarInfoDto(ItemId, true, 2.388, 2.39, "Modern Anamorphic Scope"),
            BlackBarInfoDto.Create(ItemId, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 2.388)));
    }

    [Fact]
    public void Create_RatioNearNoStandard_IsMeasuredWithoutACropRatio()
    {
        // Players crop only to standard ratios, so an odd measurement shows the whole frame
        Assert.Equal(new BlackBarInfoDto(ItemId, true, 3.2, 0, null), BlackBarInfoDto.Create(ItemId, new BlackBarMeasurement(BlackBarAccumulator.MethodVersion, Source, 3.2)));
    }
}
