namespace Knapcode.FactorioTools.OilField;

public class QualitiesTest
{
    [Theory]
    [InlineData(Quality.Normal, "normal")]
    [InlineData(Quality.Uncommon, "uncommon")]
    [InlineData(Quality.Rare, "rare")]
    [InlineData(Quality.Epic, "epic")]
    [InlineData(Quality.Legendary, "legendary")]
    public void ToBlueprintString_ReturnsLowercaseName(Quality quality, string expected)
    {
        Assert.Equal(expected, Qualities.ToBlueprintString(quality));
    }

    [Fact]
    public void ToBlueprintString_FallsBackToNormal_ForUnknownValue()
    {
        // The switch returns "normal" via its default arm. An undefined enum value must hit that default
        // rather than throwing or returning some other tier's name.
        var unknown = (Quality)42;

        Assert.Equal("normal", Qualities.ToBlueprintString(unknown));
    }

    [Fact]
    public void QualityEnum_EncodesBonusLevel_WithHiddenLevelFourSkipped()
    {
        // The integer value is the quality bonus level used to scale stats. The Factorio engine skips a
        // hidden level 4, so Legendary is level 5 (not 4) while the lower tiers are contiguous.
        Assert.Equal(0, (int)Quality.Normal);
        Assert.Equal(1, (int)Quality.Uncommon);
        Assert.Equal(2, (int)Quality.Rare);
        Assert.Equal(3, (int)Quality.Epic);
        Assert.Equal(5, (int)Quality.Legendary);
    }
}
