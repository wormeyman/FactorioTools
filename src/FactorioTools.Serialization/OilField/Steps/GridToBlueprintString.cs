using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using Knapcode.FactorioTools.Data;
using System.Collections.Generic;
using System;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;

namespace Knapcode.FactorioTools.OilField;

public static class GridToBlueprintString
{
    private static readonly IReadOnlyDictionary<string, (float Width, float Height)> EntityNameToSize = new Dictionary<string, (float Width, float Height)>()
    {
        { EntityNames.Vanilla.Beacon, (3, 3) },
        { EntityNames.Vanilla.BigElectricPole, (2, 2) },
        { EntityNames.Vanilla.MediumElectricPole, (1, 1) },
        { EntityNames.Vanilla.Pipe, (1, 1) },
        { EntityNames.Vanilla.PipeToGround, (1, 1) },
        { EntityNames.Vanilla.Pumpjack, (3, 3) },
        { EntityNames.Vanilla.HeatPipe, (1, 1) },
        { EntityNames.Vanilla.SmallElectricPole, (1, 1) },
        { EntityNames.Vanilla.Substation, (2, 2) },

        { EntityNames.AaiIndustry.SmallIronElectricPole, (1, 1) },
    };

    /// <summary>
    /// Converts an internal <see cref="Direction"/> (1.1-style 8-way values: N=0, E=2, S=4, W=6) to the value emitted in
    /// the output blueprint. Factorio 2.0 uses 16-way directions (N=0, E=4, S=8, W=12), so heat (2.0) mode doubles them.
    /// </summary>
    private static Direction ToOutputDirection(Context context, Direction direction)
    {
        if (context.Options.AddHeatPipes)
        {
            return (Direction)((int)direction * 2);
        }

        return direction;
    }

    // defines.inventory.mining_drill_modules and defines.inventory.beacon_modules in Factorio 2.0, used to target the
    // module inventory in the 2.0 "items" array form (confirmed against a real 2.0 export).
    private const int MiningDrillModuleInventory = 2;
    private const int BeaconModuleInventory = 1;

    /// <summary>
    /// Produces the value for an entity's "items" field. In 1.1 mode this is the module name-to-count dictionary; in
    /// heat (2.0) mode it is a list of <see cref="ModuleInsertPlan"/> targeting the given module inventory. Returns null
    /// when there are no modules so the field is omitted.
    /// </summary>
    private static object? ToOutputItems(Context context, Dictionary<string, int> modules, int inventory)
    {
        if (modules is null || modules.Count == 0)
        {
            return null;
        }

        if (!context.Options.AddHeatPipes)
        {
            return modules;
        }

        var plans = new List<ModuleInsertPlan>();
        var stack = 0;
        foreach (var pair in modules)
        {
            plans.Add(new ModuleInsertPlan { Name = pair.Key, Inventory = inventory, StartStack = stack, Count = pair.Value });
            stack += pair.Value;
        }

        return plans;
    }

    public static string Execute(Context context, bool addFbeOffset, bool addAvoidEntities)
    {
        var entities = new List<Entity>();
        var nextEntityNumber = 1;

        var gridIdToEntityNumber = new Dictionary<int, int>();

        int GetEntityNumber(GridEntity entity)
        {
            if (!gridIdToEntityNumber!.TryGetValue(entity.Id, out var entityNumber))
            {
                entityNumber = nextEntityNumber++;
                gridIdToEntityNumber.Add(entity.Id, entityNumber);
            }

            return entityNumber;
        }

        foreach (var location in context.Grid.EntityLocations.EnumerateItems())
        {
            var gridEntity = context.Grid[location];
            var position = new Position
            {
                X = location.X,
                Y = location.Y,
            };

            switch (gridEntity)
            {
                case PumpjackCenter pumpjackCenter:
                    entities.Add(new Entity
                    {
                        EntityNumber = nextEntityNumber++,
                        Direction = ToOutputDirection(context, pumpjackCenter.Direction),
                        Name = EntityNames.Vanilla.Pumpjack,
                        Position = position,
                        Items = ToOutputItems(context, context.Options.PumpjackModules, MiningDrillModuleInventory),
                    });
                    break;
                case PumpjackSide:
                    // Ignore
                    break;
                case UndergroundPipe undergroundPipe:
                    entities.Add(new Entity
                    {
                        EntityNumber = nextEntityNumber++,
                        Direction = ToOutputDirection(context, undergroundPipe.Direction),
                        Name = EntityNames.Vanilla.PipeToGround,
                        Position = position,
                    });
                    break;
                case HeatPipe:
                    entities.Add(new Entity
                    {
                        EntityNumber = nextEntityNumber++,
                        Name = context.Options.HeatPipeEntityName,
                        Position = position,
                    });
                    break;
                case Pipe:
                    entities.Add(new Entity
                    {
                        EntityNumber = nextEntityNumber++,
                        Name = EntityNames.Vanilla.Pipe,
                        Position = position,
                    });
                    break;
                case ElectricPoleCenter electricPole:
                    if (context.Options.ElectricPoleWidth % 2 == 0)
                    {
                        position.X += 0.5f;
                    }

                    if (context.Options.ElectricPoleHeight % 2 == 0)
                    {
                        position.Y += 0.5f;
                    }

                    entities.Add(new Entity
                    {
                        EntityNumber = GetEntityNumber(electricPole),
                        Name = context.Options.ElectricPoleEntityName,
                        Position = position,
                        Neighbours = electricPole
                            .Neighbors
                            .Select(id => context.Grid[context.Grid.EntityIdToLocation[id]]!)
                            .Select(GetEntityNumber)
                            .ToArray(),
                    });
                    break;
                case ElectricPoleSide:
                    // Ignore
                    break;
                case BeaconCenter beacon:
                    if (context.Options.BeaconWidth % 2 == 0)
                    {
                        position.X += 0.5f;
                    }

                    if (context.Options.BeaconHeight % 2 == 0)
                    {
                        position.Y += 0.5f;
                    }

                    entities.Add(new Entity
                    {
                        EntityNumber = nextEntityNumber++,
                        Name = context.Options.BeaconEntityName,
                        Position = position,
                        Items = ToOutputItems(context, context.Options.BeaconModules, BeaconModuleInventory),
                    });
                    break;
                case AvoidEntity:
                    if (addAvoidEntities)
                    {
                        entities.Add(new Entity
                        {
                            EntityNumber = nextEntityNumber++,
                            Name = EntityNames.Vanilla.Wall,
                            Position = position,
                        });
                    }
                    break;
                case BeaconSide:
                case TemporaryEntity:
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        var blueprint = new Blueprint
        {
            Icons = new[]
            {
                new Icon
                {
                    Index = 1,
                    Signal = new SignalID
                    {
                        Name = EntityNames.Vanilla.Pumpjack,
                        Type = SignalTypes.Vanilla.Item,
                    }
                }
            },
            // Heat pipes / the Aquilo freezing mechanic are a Factorio 2.0 (Space Age) feature, so emit a 2.0 version
            // string in heat mode. Otherwise keep the established 1.1 version for unchanged output.
            Version = context.Options.AddHeatPipes ? FormatVersion(2, 0, 32, 0) : FormatVersion(1, 1, 101, 1),
            Item = ItemNames.Vanilla.Blueprint,
            Entities = entities.ToArray(),
        };

        return SerializeBlueprint(blueprint, addFbeOffset);
    }

    /// <summary>
    /// Source: https://wiki.factorio.com/Version_string_format
    /// </summary>
    public static (ushort major, ushort minor, ushort patch, ushort developer) ParseVersion(ulong version)
    {
        return (
            (ushort)((version >> 48) & 0xFFFF),
            (ushort)((version >> 32) & 0xFFFF),
            (ushort)((version >> 16) & 0xFFFF),
            (ushort)(version & 0xFFFF)
        );
    }

    public static ulong FormatVersion(ushort major, ushort minor, ushort patch, ushort developer)
    {
        return ((ulong)major << 48) | ((ulong)minor << 32) | ((ulong)patch << 16) | developer;
    }

    public static string SerializeBlueprint(Blueprint blueprint, bool addFbeOffset)
    {
        // FBE applies some offset to the blueprint coordinates. This makes it hard to compare the grid used in memory
        // with the rendered blueprint in FBE. To account for this, we can add an entity to the corner of the
        // blueprint with a position that makes FBE keep the original entity positions used by the grid.
        if (addFbeOffset && blueprint.Entities.Length > 0)
        {
            var maxX = float.MinValue;
            var maxY = float.MinValue;
            var maxEntityNumber = int.MinValue;

            foreach (var entity in blueprint.Entities)
            {
                (var width, var height) = EntityNameToSize[entity.Name];
                maxX = Math.Max(maxX, entity.Position.X + width / 2);
                maxY = Math.Max(maxY, entity.Position.Y + height / 2);
                maxEntityNumber = Math.Max(maxEntityNumber, entity.EntityNumber);
            }

            blueprint.Entities = blueprint.Entities.Append(new Entity
            {
                EntityNumber = maxEntityNumber + 1,
                Name = EntityNames.Vanilla.Wall,
                Position = new Position
                {
                    X = (float)-Math.Ceiling(maxX),
                    Y = (float)-Math.Ceiling(maxY),
                },
            }).ToArray();
        }

        var root = new BlueprintRoot { Blueprint = blueprint };

        var json = JsonSerializer.Serialize(root, typeof(BlueprintRoot), BlueprintSerialization.Context);

        var bytes = Encoding.UTF8.GetBytes(json);
        using var outputStream = new MemoryStream();

        using var zlibStream = new ZLibStream(outputStream, CompressionLevel.Optimal);
        zlibStream.Write(bytes, 0, bytes.Length);
        zlibStream.Flush();
        zlibStream.Dispose();
        var base64 = Convert.ToBase64String(outputStream.ToArray());

        return '0' + base64;
    }
}
