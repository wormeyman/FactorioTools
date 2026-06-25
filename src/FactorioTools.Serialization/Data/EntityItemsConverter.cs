using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Knapcode.FactorioTools.Data;

/// <summary>
/// A request to insert modules of one item type into an entity inventory, used to emit the Factorio 2.0 blueprint
/// "items" array. The count is expressed as one inventory stack per module (stacks StartStack .. StartStack + Count - 1).
/// </summary>
public class ModuleInsertPlan
{
    public string Name { get; set; } = null!;
    public int Inventory { get; set; }
    public int StartStack { get; set; }
    public int Count { get; set; }
    public string? Quality { get; set; }
}

/// <summary>
/// Serializes an entity's "items" field, which has two incompatible shapes across Factorio versions:
/// - 1.1: an object mapping item name to count, e.g. {"productivity-module-3": 2}. Held as <see cref="Dictionary{String,Int32}"/>.
/// - 2.0: an array of insert plans, e.g. [{"id":{"name":"..."},"items":{"in_inventory":[{"inventory":2,"stack":0}]}}].
///   Held as a <see cref="List{ModuleInsertPlan}"/>.
/// The value carried by <c>Entity.Items</c> (typed <c>object?</c>) is one of those two. Reading converts the 1.1 object
/// form back to a dictionary; the 2.0 array form is not consumed by the planner (only pumpjack positions are), so it is
/// skipped.
/// </summary>
public class EntityItemsConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var dictionary = new Dictionary<string, int>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString()!;
                reader.Read();
                dictionary[name] = reader.GetInt32();
            }

            return dictionary;
        }

        // 2.0 array form (or anything else) - not needed by the planner, so consume and ignore it.
        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        if (value is Dictionary<string, int> dictionary)
        {
            writer.WriteStartObject();
            foreach (var pair in dictionary)
            {
                writer.WriteNumber(pair.Key, pair.Value);
            }

            writer.WriteEndObject();
            return;
        }

        if (value is List<ModuleInsertPlan> plans)
        {
            writer.WriteStartArray();
            foreach (var plan in plans)
            {
                writer.WriteStartObject();

                writer.WritePropertyName("id");
                writer.WriteStartObject();
                writer.WriteString("name", plan.Name);
                if (plan.Quality is not null)
                {
                    writer.WriteString("quality", plan.Quality);
                }
                writer.WriteEndObject();

                writer.WritePropertyName("items");
                writer.WriteStartObject();
                writer.WritePropertyName("in_inventory");
                writer.WriteStartArray();
                for (var i = 0; i < plan.Count; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("inventory", plan.Inventory);
                    writer.WriteNumber("stack", plan.StartStack + i);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            return;
        }

        writer.WriteNullValue();
    }
}

/// <summary>
/// Shared source-generated serialization context configured with <see cref="EntityItemsConverter"/>, used for both
/// parsing input blueprints and emitting output blueprints so the "items" field round-trips correctly.
/// </summary>
public static class BlueprintSerialization
{
    public static readonly BlueprintSerializationContext Context = Create();

    private static BlueprintSerializationContext Create()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new EntityItemsConverter());
        return new BlueprintSerializationContext(options);
    }
}
