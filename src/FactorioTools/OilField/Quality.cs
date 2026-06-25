namespace Knapcode.FactorioTools.OilField;

/// <summary>
/// A Factorio 2.0 quality tier. The integer value is the quality bonus level used to scale
/// quality-affected stats (the engine skips a hidden level 4, so legendary is level 5).
/// </summary>
public enum Quality
{
    Normal = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 5,
}

public static class Qualities
{
    /// <summary>
    /// The lowercase quality name used in blueprint JSON (e.g. "legendary"). "normal" is the default
    /// and is omitted from output by callers.
    /// </summary>
    public static string ToBlueprintString(Quality quality)
    {
        switch (quality)
        {
            case Quality.Uncommon:
                return "uncommon";
            case Quality.Rare:
                return "rare";
            case Quality.Epic:
                return "epic";
            case Quality.Legendary:
                return "legendary";
            default:
                return "normal";
        }
    }
}
