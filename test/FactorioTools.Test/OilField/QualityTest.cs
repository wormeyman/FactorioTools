namespace Knapcode.FactorioTools.OilField;

public class QualityTest
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
    public void EnumValues_AreQualityBonusLevels()
    {
        Assert.Equal(0, (int)Quality.Normal);
        Assert.Equal(1, (int)Quality.Uncommon);
        Assert.Equal(2, (int)Quality.Rare);
        Assert.Equal(3, (int)Quality.Epic);
        Assert.Equal(5, (int)Quality.Legendary);
    }
}
