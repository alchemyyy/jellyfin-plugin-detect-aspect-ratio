using System;
using System.Linq;
using Jellyfin.Plugin.DetectAspectRatio.Analysis;
using Xunit;

namespace Jellyfin.Plugin.DetectAspectRatio.Tests;

public sealed class AspectRatioFunctionsTests
{
    [Theory]
    [InlineData(1.78, 1.78)]
    [InlineData(2.39, 2.39)]
    [InlineData(1.33, 1.33)]
    [InlineData(2.76, 2.76)]
    [InlineData(1.85, 1.85)]
    public void Snap_ExactMatch_ReturnsTheStandard(double measured, double expected)
    {
        Assert.Equal(expected, AspectRatioFunctions.Snap(measured)?.Ratio);
    }

    [Theory]
    [InlineData(1.80, 1.78)]
    [InlineData(2.42, 2.40)]
    [InlineData(1.35, 1.33)]
    [InlineData(2.391, 2.39)]
    [InlineData(1.777, 1.78)]
    [InlineData(2.83, 2.76)]
    public void Snap_WithinTolerance_ReturnsTheNearestStandard(double measured, double expected)
    {
        Assert.Equal(expected, AspectRatioFunctions.Snap(measured)?.Ratio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(0.92)]
    [InlineData(2.84)]
    [InlineData(3.5)]
    public void Snap_OutsideTolerance_ReturnsNull(double measured)
    {
        Assert.Null(AspectRatioFunctions.Snap(measured));
    }

    [Theory]
    [InlineData(1.78, "16:9 HDTV")]
    [InlineData(2.39, "Modern Anamorphic Scope")]
    [InlineData(1.33, "4:3 Standard TV")]
    [InlineData(2.76, "Ultra Panavision 70")]
    public void Snap_ReturnsTheIndustryName(double measured, string expected)
    {
        Assert.Equal(expected, AspectRatioFunctions.Snap(measured)?.Name);
    }

    [Fact]
    public void Snap_Tie_ReturnsTheLowerStandard()
    {
        // 2.395 is equally far from 2.39 and 2.40
        Assert.Equal(2.39, AspectRatioFunctions.Snap(2.395)?.Ratio);
    }

    [Fact]
    public void StandardRatios_AreAscendingAndUnique()
    {
        double[] ratios = AspectRatioFunctions.StandardRatios.Select(standard => standard.Ratio).ToArray();

        Assert.Equal(ratios.Order(), ratios);
        Assert.Equal(ratios.Length, ratios.Distinct().Count());
        Assert.All(AspectRatioFunctions.StandardRatios, standard => Assert.False(string.IsNullOrWhiteSpace(standard.Name)));
        Assert.Equal(ratios.Length, AspectRatioFunctions.StandardRatios.Select(standard => standard.Name).Distinct(StringComparer.Ordinal).Count());
    }
}
