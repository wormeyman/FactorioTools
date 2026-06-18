using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Knapcode.FactorioTools.Data;

public class Entity
{
    [JsonPropertyName("entity_number")]
    public int EntityNumber { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("position")]
    public Position Position { get; set; } = null!;

    [JsonPropertyName("direction")]
    public Direction? Direction { get; set; }

    // Either a Dictionary<string, int> (Factorio 1.1 "items" object) or a List<ModuleInsertPlan> (Factorio 2.0 "items"
    // array). The shape is handled by EntityItemsConverter in the serialization project so the core library stays free
    // of serialization logic. See GridToBlueprintString for how each version is produced.
    [JsonPropertyName("items")]
    public object? Items { get; set; }

    [JsonPropertyName("neighbours")]
    public int[]? Neighbours { get; set; }
}
