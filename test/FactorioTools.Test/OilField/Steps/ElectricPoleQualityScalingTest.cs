namespace Knapcode.FactorioTools.OilField;

public class ElectricPoleQualityScalingTest
{
    // Big electric pole base geometry: supply 4x4, wire reach 32. Each quality level adds 2 to supply
    // width/height and wire reach, and Legendary is level 5 (the hidden level 4 is skipped). The wire
    // reach values match the Factorio wiki: 32/34/36/38/42 for Normal/Uncommon/Rare/Epic/Legendary.
    [Theory]
    [InlineData(Quality.Normal, 4, 4, 32d, 1024d)]
    [InlineData(Quality.Uncommon, 6, 6, 34d, 1156d)]
    [InlineData(Quality.Rare, 8, 8, 36d, 1296d)]
    [InlineData(Quality.Epic, 10, 10, 38d, 1444d)]
    [InlineData(Quality.Legendary, 14, 14, 42d, 1764d)]
    public void BigElectricPole_ScalesGeometryByQuality(
        Quality quality,
        int expectedSupplyWidth,
        int expectedSupplyHeight,
        double expectedWireReach,
        double expectedWireReachSquared)
    {
        var options = OilFieldOptions.ForBigElectricPole;
        options.ElectricPoleQuality = quality;

        var context = InitializeContext.GetEmpty(options, width: 20, height: 20);

        Assert.Equal(expectedSupplyWidth, context.ElectricPoleSupplyWidthWithQuality);
        Assert.Equal(expectedSupplyHeight, context.ElectricPoleSupplyHeightWithQuality);
        Assert.Equal(expectedWireReach, context.ElectricPoleWireReachWithQuality);
        Assert.Equal(expectedWireReachSquared, context.ElectricPoleWireReachSquaredWithQuality);
    }

    // A second preset (medium pole: base supply 7x7, wire reach 9) confirms the base values flow through
    // the +2*level scaling rather than the big-pole numbers being hard-coded anywhere.
    [Theory]
    [InlineData(Quality.Normal, 7, 7, 9d, 81d)]
    [InlineData(Quality.Rare, 11, 11, 13d, 169d)]
    [InlineData(Quality.Legendary, 17, 17, 19d, 361d)]
    public void MediumElectricPole_ScalesGeometryByQuality(
        Quality quality,
        int expectedSupplyWidth,
        int expectedSupplyHeight,
        double expectedWireReach,
        double expectedWireReachSquared)
    {
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ElectricPoleQuality = quality;

        var context = InitializeContext.GetEmpty(options, width: 20, height: 20);

        Assert.Equal(expectedSupplyWidth, context.ElectricPoleSupplyWidthWithQuality);
        Assert.Equal(expectedSupplyHeight, context.ElectricPoleSupplyHeightWithQuality);
        Assert.Equal(expectedWireReach, context.ElectricPoleWireReachWithQuality);
        Assert.Equal(expectedWireReachSquared, context.ElectricPoleWireReachSquaredWithQuality);
    }

    [Fact]
    public void SupplyWidthAndHeight_ScaleIndependently()
    {
        // The presets above all have a square supply area, so they cannot tell whether the height field is
        // mistakenly fed the width base value (or vice versa). Force an asymmetric supply area to pin each
        // dimension to its own base: width 4 -> 4 + 2*2 = 8, height 6 -> 6 + 2*2 = 10 at Rare (level 2).
        var options = OilFieldOptions.ForBigElectricPole;
        options.ElectricPoleSupplyWidth = 4;
        options.ElectricPoleSupplyHeight = 6;
        options.ElectricPoleQuality = Quality.Rare;

        var context = InitializeContext.GetEmpty(options, width: 20, height: 20);

        Assert.Equal(8, context.ElectricPoleSupplyWidthWithQuality);
        Assert.Equal(10, context.ElectricPoleSupplyHeightWithQuality);
    }

    [Fact]
    public void NormalQuality_LeavesBaseGeometryUnchanged()
    {
        // Level 0 must add nothing: the WithQuality fields should equal the preset's raw option values.
        var options = OilFieldOptions.ForBigElectricPole;
        options.ElectricPoleQuality = Quality.Normal;

        var context = InitializeContext.GetEmpty(options, width: 20, height: 20);

        Assert.Equal(options.ElectricPoleSupplyWidth, context.ElectricPoleSupplyWidthWithQuality);
        Assert.Equal(options.ElectricPoleSupplyHeight, context.ElectricPoleSupplyHeightWithQuality);
        Assert.Equal(options.ElectricPoleWireReach, context.ElectricPoleWireReachWithQuality);
        Assert.Equal(
            options.ElectricPoleWireReach * options.ElectricPoleWireReach,
            context.ElectricPoleWireReachSquaredWithQuality);
    }
}
