# Factorio Quality Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Factorio 2.0 quality (uncommon/rare/epic/legendary) as a per-item-type option for pumpjacks, beacons, electric poles, and their modules, with electric-pole quality also enlarging the planner's supply area and wire reach.

**Architecture:** A new `Quality` enum lives in the core library. Five quality fields are added to `OilFieldOptions`. Most qualities are output-only and are stamped into the emitted 2.0 blueprint. Electric-pole quality is the exception: it scales the supply area and wire reach the planner uses, computed once onto the `Context`. Blueprint output becomes Factorio 2.0 unconditionally (the previous heat-only 2.0 path becomes the only path). The Vue UI exposes the qualities under the existing advanced-options toggle as CSS/SVG badges. The CLI sample command gets quality flags. The transpiled Lua is regenerated.

**Tech Stack:** C# (.NET 10), xUnit + Verify, System.Text.Json source-gen, System.CommandLine, Vue 3 + Pinia + Vite + Vitest, swagger-typescript-api, CSharp.lua (Lua 5.2).

## Global Constraints

- Use hyphens, not em/en dashes, in all files.
- Quality enum integer values are the bonus level: `Normal = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 5` (the engine skips a hidden level 4).
- Electric-pole quality scaling (verified for small/medium/substation; confirm big pole during Task 3): `effective = base + 2 * level` for supply width, supply height, and wire reach. Footprint width/height are unchanged by quality.
- Blueprint output is always Factorio 2.0 (version `2.0.x`, 16-way directions, 2.0 module `items` array). No 1.1 output path remains.
- Blueprint quality strings are lowercase: `normal`, `uncommon`, `rare`, `epic`, `legendary`. `normal` is omitted from output (no `quality` field).
- The contract serializes enums by name via `JsonStringEnumConverter` (so `Quality.Legendary` -> `"Legendary"` over the API/WASM boundary). The lowercase blueprint-string mapping is a separate concern handled only at emission.
- The core library must stay Lua-safe: no LINQ, `yield return`, try/catch, named tuples, or struct dictionary keys in code paths transpiled to Lua. Use simple `switch`/`if` and plain loops.
- Build and test under both the default configuration and `dotnet test /p:UseLuaSettings=true` before considering core work done.
- Run all `dotnet`/`npm` commands from the repository root unless a step says otherwise. The repo root is `/Users/ericjohnson/GitHub/FactorioTools`. The shell is fish.
- The work branch is `feat/quality-support` (already created and holding the spec commit).

---

### Task 1: Quality enum and blueprint-string mapping

**Files:**
- Create: `src/FactorioTools/OilField/Quality.cs`
- Modify: `src/FactorioTools.Contract/ContractJsonContext.cs` (register the enum converter, near the existing `JsonStringEnumConverter<PipeStrategy>` at line ~35)
- Test: `test/FactorioTools.Test/OilField/QualityTest.cs`

**Interfaces:**
- Produces: `enum Quality { Normal = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 5 }` and `static class Qualities { string ToBlueprintString(Quality quality) }` returning the lowercase name, both in namespace `Knapcode.FactorioTools.OilField`.

- [ ] **Step 1: Write the failing test**

Create `test/FactorioTools.Test/OilField/QualityTest.cs`:

```csharp
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
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~QualityTest"`
Expected: FAIL with a compile error (`Quality` / `Qualities` do not exist).

- [ ] **Step 3: Create the enum and mapping**

Create `src/FactorioTools/OilField/Quality.cs`:

```csharp
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
```

- [ ] **Step 4: Register the contract enum converter**

In `src/FactorioTools.Contract/ContractJsonContext.cs`, after the line
`options.Converters.Add(new JsonStringEnumConverter<BeaconStrategy>());` add:

```csharp
        options.Converters.Add(new JsonStringEnumConverter<Quality>());
```

(If the file does not already have `using Knapcode.FactorioTools.OilField;`, add it; it almost certainly does because it references `PipeStrategy`.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~QualityTest"`
Expected: PASS (6 cases).

- [ ] **Step 6: Commit**

```bash
git add src/FactorioTools/OilField/Quality.cs src/FactorioTools.Contract/ContractJsonContext.cs test/FactorioTools.Test/OilField/QualityTest.cs
git commit -m "Add Quality enum and blueprint-string mapping"
```

---

### Task 2: Quality fields on OilFieldOptions

**Files:**
- Modify: `src/FactorioTools/OilField/OilFieldOptions.cs` (add fields after `BeaconModules`, ~line 266)
- Test: `test/FactorioTools.Test/OilField/OilFieldOptionsTest.cs` (add to the existing class)

**Interfaces:**
- Produces: `OilFieldOptions.PumpjackQuality`, `.BeaconQuality`, `.ElectricPoleQuality`, `.PumpjackModuleQuality`, `.BeaconModuleQuality`, all of type `Quality`, default `Quality.Normal`.

- [ ] **Step 1: Write the failing test**

Add to `test/FactorioTools.Test/OilField/OilFieldOptionsTest.cs`:

```csharp
    [Fact]
    public void QualityDefaultsAreNormal()
    {
        var options = new OilFieldOptions();
        Assert.Equal(Quality.Normal, options.PumpjackQuality);
        Assert.Equal(Quality.Normal, options.BeaconQuality);
        Assert.Equal(Quality.Normal, options.ElectricPoleQuality);
        Assert.Equal(Quality.Normal, options.PumpjackModuleQuality);
        Assert.Equal(Quality.Normal, options.BeaconModuleQuality);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~OilFieldOptionsTest.QualityDefaultsAreNormal"`
Expected: FAIL with a compile error (properties do not exist).

- [ ] **Step 3: Add the properties**

In `src/FactorioTools/OilField/OilFieldOptions.cs`, after the `BeaconModules` property (the last property, ending around line 266), add:

```csharp

    /// <summary>
    /// The quality of the pumpjack entities in the output blueprint. Output-only; does not affect planning.
    /// </summary>
    public Quality PumpjackQuality { get; set; } = Quality.Normal;

    /// <summary>
    /// The quality of the beacon entities in the output blueprint. Output-only; does not affect planning.
    /// </summary>
    public Quality BeaconQuality { get; set; } = Quality.Normal;

    /// <summary>
    /// The quality of the electric pole entities. Higher quality enlarges the supply area and wire reach
    /// the planner uses (see Context), so this affects planning, not just output.
    /// </summary>
    public Quality ElectricPoleQuality { get; set; } = Quality.Normal;

    /// <summary>
    /// The quality of the modules inserted into pumpjacks. Output-only; does not affect planning.
    /// </summary>
    public Quality PumpjackModuleQuality { get; set; } = Quality.Normal;

    /// <summary>
    /// The quality of the modules inserted into beacons. Output-only; does not affect planning.
    /// </summary>
    public Quality BeaconModuleQuality { get; set; } = Quality.Normal;
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~OilFieldOptionsTest.QualityDefaultsAreNormal"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/FactorioTools/OilField/OilFieldOptions.cs test/FactorioTools.Test/OilField/OilFieldOptionsTest.cs
git commit -m "Add per-item-type quality options"
```

---

### Task 3: Electric-pole quality scaling on Context

**Files:**
- Modify: `src/FactorioTools/OilField/Models/Context.cs` (add four fields)
- Modify: `src/FactorioTools/OilField/Steps/InitializeContext.cs` (set the fields when building Context, in the `new Context { ... }` at line ~71)
- Modify: `src/FactorioTools/OilField/Steps/AddElectricPoles.cs` (read effective values; change `AreElectricPolesConnected` to take `Context`)
- Modify: `src/FactorioTools/OilField/Helpers.cs` (lines 143-144 and 688-689 read effective supply area)
- Test: `test/FactorioTools.Test/OilField/Steps/InitializeContextTest.cs` (add cases)

**Interfaces:**
- Consumes: `Quality` (Task 1), `OilFieldOptions.ElectricPoleQuality` (Task 2).
- Produces on `Context`: `int ElectricPoleSupplyWidthWithQuality`, `int ElectricPoleSupplyHeightWithQuality`, `double ElectricPoleWireReachWithQuality`, `double ElectricPoleWireReachSquaredWithQuality`.
- Changes signature: `AddElectricPoles.AreElectricPolesConnected(Location a, Location b, Context context)` (was `(Location, Location, OilFieldOptions)`).

- [ ] **Step 1: Write the failing test**

Add to `test/FactorioTools.Test/OilField/Steps/InitializeContextTest.cs` (namespace `Knapcode.FactorioTools.OilField`; it already has `using` for the project). If the file has no usings for `OilFieldOptions`/`InitializeContext`, they are in the same namespace so none are needed.

```csharp
    [Theory]
    // medium pole base 7x7 / 9; substation base 18x18 / 18. effective = base + 2*level.
    [InlineData(7, 9, Quality.Normal, 7, 9.0)]
    [InlineData(7, 9, Quality.Uncommon, 9, 11.0)]
    [InlineData(7, 9, Quality.Rare, 11, 13.0)]
    [InlineData(7, 9, Quality.Epic, 13, 15.0)]
    [InlineData(7, 9, Quality.Legendary, 17, 19.0)]
    [InlineData(18, 18, Quality.Legendary, 28, 28.0)]
    public void ElectricPoleQualityScalesSupplyAndReach(
        int baseSupply, double baseReach, Quality quality, int expectedSupply, double expectedReach)
    {
        var options = new OilFieldOptions
        {
            ElectricPoleSupplyWidth = baseSupply,
            ElectricPoleSupplyHeight = baseSupply,
            ElectricPoleWireReach = baseReach,
            ElectricPoleQuality = quality,
        };

        var context = InitializeContext.GetEmpty(options, width: 10, height: 10);

        Assert.Equal(expectedSupply, context.ElectricPoleSupplyWidthWithQuality);
        Assert.Equal(expectedSupply, context.ElectricPoleSupplyHeightWithQuality);
        Assert.Equal(expectedReach, context.ElectricPoleWireReachWithQuality);
        Assert.Equal(expectedReach * expectedReach, context.ElectricPoleWireReachSquaredWithQuality);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~InitializeContextTest.ElectricPoleQualityScalesSupplyAndReach"`
Expected: FAIL with a compile error (the `...WithQuality` members do not exist).

- [ ] **Step 3: Add the Context fields**

In `src/FactorioTools/OilField/Models/Context.cs`, add after `public required int[] LocationToAdjacentCount { get; set; }` (line ~18):

```csharp

    /// <summary>
    /// The electric pole supply width after applying <see cref="OilFieldOptions.ElectricPoleQuality"/>
    /// (base + 2 * quality level). Set by <see cref="InitializeContext"/>. Footprint width is unchanged.
    /// </summary>
    public int ElectricPoleSupplyWidthWithQuality { get; set; }

    /// <summary>
    /// The electric pole supply height after applying quality (base + 2 * quality level).
    /// </summary>
    public int ElectricPoleSupplyHeightWithQuality { get; set; }

    /// <summary>
    /// The electric pole wire reach after applying quality (base + 2 * quality level).
    /// </summary>
    public double ElectricPoleWireReachWithQuality { get; set; }

    /// <summary>
    /// The square of <see cref="ElectricPoleWireReachWithQuality"/>, precomputed for hot-path distance checks.
    /// </summary>
    public double ElectricPoleWireReachSquaredWithQuality { get; set; }
```

- [ ] **Step 4: Set the fields in InitializeContext**

In `src/FactorioTools/OilField/Steps/InitializeContext.cs`, find the `var context = new Context { ... };` (or `return new Context { ... };`) at line ~71. If it is a `return new Context { ... };`, change it to assign to a local first so the fields can be set:

```csharp
        var context = new Context
        {
            // ... keep all existing initializers unchanged ...
        };

        var poleLevel = (int)options.ElectricPoleQuality;
        context.ElectricPoleSupplyWidthWithQuality = options.ElectricPoleSupplyWidth + 2 * poleLevel;
        context.ElectricPoleSupplyHeightWithQuality = options.ElectricPoleSupplyHeight + 2 * poleLevel;
        context.ElectricPoleWireReachWithQuality = options.ElectricPoleWireReach + 2 * poleLevel;
        context.ElectricPoleWireReachSquaredWithQuality =
            context.ElectricPoleWireReachWithQuality * context.ElectricPoleWireReachWithQuality;

        return context;
```

(If the method already ends with `var context = new Context {...}; ... return context;`, just insert the five assignment lines before the existing `return context;`.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~InitializeContextTest.ElectricPoleQualityScalesSupplyAndReach"`
Expected: PASS (6 cases).

- [ ] **Step 6: Route AddElectricPoles through the effective values**

In `src/FactorioTools/OilField/Steps/AddElectricPoles.cs`:

Change the method signature and body at lines ~192-195 from:

```csharp
    public static bool AreElectricPolesConnected(Location a, Location b, OilFieldOptions options)
    {
        return GetElectricPoleDistanceSquared(a, b, options) <= options.ElectricPoleWireReachSquared;
    }
```

to:

```csharp
    public static bool AreElectricPolesConnected(Location a, Location b, Context context)
    {
        return GetElectricPoleDistanceSquared(a, b, context.Options) <= context.ElectricPoleWireReachSquaredWithQuality;
    }
```

Update its four callers to pass `context` instead of `context.Options`:
- Line ~185: `if (AreElectricPolesConnected(center, other, context))`
- Line ~493: `if (AreElectricPolesConnected(candidate, electricPoleList[i], context))`
- Line ~670: `if (!AreElectricPolesConnected(idealLine[0], idealLine[idealIndex], context))`
- Line ~716: `&& AreElectricPolesConnected(idealLine[0], neighbors[i], context)`

Update the two inline wire-reach-squared comparisons:
- Line ~588: change `<= context.Options.ElectricPoleWireReachSquared` to `<= context.ElectricPoleWireReachSquaredWithQuality`
- Line ~643: change `<= context.Options.ElectricPoleWireReachSquared` to `<= context.ElectricPoleWireReachSquaredWithQuality`

Update the two `Math.Ceiling` wire-reach reads:
- Line ~373: change `context.Options.ElectricPoleWireReach` to `context.ElectricPoleWireReachWithQuality`
- Line ~667: change `context.Options.ElectricPoleWireReach` to `context.ElectricPoleWireReachWithQuality`

Note: `GetElectricPoleDistanceSquared` keeps taking `OilFieldOptions` (it uses only the footprint width/height, which quality does not change).

- [ ] **Step 7: Route Helpers.cs supply-area reads through the effective values**

In `src/FactorioTools/OilField/Helpers.cs`, at lines ~143-144 and ~688-689, change:

```csharp
            context.Options.ElectricPoleSupplyWidth,
            context.Options.ElectricPoleSupplyHeight,
```

to:

```csharp
            context.ElectricPoleSupplyWidthWithQuality,
            context.ElectricPoleSupplyHeightWithQuality,
```

(Confirm the surrounding call has `context` in scope at both sites - it does in both current usages.)

- [ ] **Step 8: Add an end-to-end test that legendary poles reduce pole count**

Add to `test/FactorioTools.Test/OilField/PlannerTest.cs` (class `PlannerTest : BasePlannerTest`):

```csharp
    [Fact]
    public void LegendaryElectricPolesReduceOrMatchPoleCount()
    {
        var blueprintString = SmallListBlueprintStrings[0];

        var normal = OilFieldOptions.ForMediumElectricPole;
        normal.ValidateSolution = true;
        var (normalContext, _) = Planner.Execute(normal, ParseBlueprint.Execute(blueprintString));
        var normalPoles = normalContext.Grid.GetEntities().OfType<ElectricPoleCenter>().Count();

        var legendary = OilFieldOptions.ForMediumElectricPole;
        legendary.ValidateSolution = true;
        legendary.ElectricPoleQuality = Quality.Legendary;
        var (legendaryContext, _) = Planner.Execute(legendary, ParseBlueprint.Execute(blueprintString));
        var legendaryPoles = legendaryContext.Grid.GetEntities().OfType<ElectricPoleCenter>().Count();

        Assert.True(
            legendaryPoles <= normalPoles,
            $"legendary used {legendaryPoles} poles vs normal {normalPoles}");
    }
```

Verify `Planner.Execute` returns a `(Context, ...)`-shaped tuple as used elsewhere in this file (see `AllowsElectricPolesToNotBePlanned`, which does `var (context, _) = Planner.Execute(...)`). Match that exact deconstruction.

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~InitializeContextTest.ElectricPoleQualityScalesSupplyAndReach|FullyQualifiedName~PlannerTest.LegendaryElectricPolesReduceOrMatchPoleCount"`
Expected: PASS.

- [ ] **Step 10: Run the full core test suite to confirm no snapshot churn**

Run: `dotnet test`
Expected: PASS with no changed `*.verified.txt` snapshots (pole quality defaults to Normal everywhere existing tests use).

- [ ] **Step 11: Commit**

```bash
git add src/FactorioTools/OilField/Models/Context.cs src/FactorioTools/OilField/Steps/InitializeContext.cs src/FactorioTools/OilField/Steps/AddElectricPoles.cs src/FactorioTools/OilField/Helpers.cs test/FactorioTools.Test/OilField/Steps/InitializeContextTest.cs test/FactorioTools.Test/OilField/PlannerTest.cs
git commit -m "Scale electric pole supply area and wire reach by quality"
```

---

### Task 4: Always-2.0 output and entity quality emission

**Files:**
- Modify: `src/FactorioTools/Data/Entity.cs` (add `Quality` property)
- Modify: `src/FactorioTools.Serialization/OilField/Steps/GridToBlueprintString.cs` (always-2.0 version + direction + items; stamp entity quality)
- Test: `test/FactorioTools.Test/OilField/PlannerTest.cs`

**Interfaces:**
- Consumes: `Qualities.ToBlueprintString` (Task 1), `OilFieldOptions.PumpjackQuality`/`.BeaconQuality`/`.ElectricPoleQuality` (Task 2).
- Produces: `Entity.Quality` (nullable string, JSON name `quality`). A private helper `string? ToOutputQuality(Quality quality)` in `GridToBlueprintString` returning null for `Normal`, else the lowercase string.

- [ ] **Step 1: Write the failing test**

Add to `test/FactorioTools.Test/OilField/PlannerTest.cs`:

```csharp
    [Fact]
    public void EmitsTwoPointZeroAndEntityQualityWithoutHeat()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        options.PumpjackQuality = Quality.Legendary;
        options.BeaconQuality = Quality.Rare;
        options.ElectricPoleQuality = Quality.Uncommon;
        var blueprint = ParseBlueprint.Execute(SmallListBlueprintStrings[0]);
        var (context, _) = Planner.Execute(options, blueprint);

        // Act
        var blueprintString = GridToBlueprintString.Execute(context, addFbeOffset: false, addAvoidEntities: false);
        var parsed = ParseBlueprint.Execute(blueprintString);

        // Assert: 2.0 version even though heat is off
        var (major, _, _, _) = GridToBlueprintString.ParseVersion(parsed.Version);
        Assert.Equal(2, major);

        // Assert: quality stamped on the right entities
        Assert.Contains(parsed.Entities, e => e.Name == EntityNames.Vanilla.Pumpjack && e.Quality == "legendary");
        Assert.Contains(parsed.Entities, e => e.Name == EntityNames.Vanilla.Beacon && e.Quality == "rare");
        Assert.Contains(parsed.Entities, e => e.Name == options.ElectricPoleEntityName && e.Quality == "uncommon");
    }

    [Fact]
    public void OmitsQualityFieldWhenNormal()
    {
        var options = OilFieldOptions.ForMediumElectricPole; // all qualities default Normal
        var blueprint = ParseBlueprint.Execute(SmallListBlueprintStrings[0]);
        var (context, _) = Planner.Execute(options, blueprint);

        var blueprintString = GridToBlueprintString.Execute(context, addFbeOffset: false, addAvoidEntities: false);
        var parsed = ParseBlueprint.Execute(blueprintString);

        Assert.All(parsed.Entities, e => Assert.Null(e.Quality));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.EmitsTwoPointZeroAndEntityQualityWithoutHeat|FullyQualifiedName~PlannerTest.OmitsQualityFieldWhenNormal"`
Expected: FAIL with a compile error (`Entity.Quality` does not exist).

- [ ] **Step 3: Add Quality to the Entity model**

In `src/FactorioTools/Data/Entity.cs`, add after the `Items` property (before `Neighbours`):

```csharp
    [JsonPropertyName("quality")]
    public string? Quality { get; set; }
```

(System.Text.Json round-trips this automatically because the converter only intercepts `items`; with `DefaultIgnoreCondition = WhenWritingNull`, a null quality is omitted on write.)

- [ ] **Step 4: Make emission always 2.0 and add the quality helper**

In `src/FactorioTools.Serialization/OilField/Steps/GridToBlueprintString.cs`:

Change `ToOutputDirection` (lines ~35-43) to always use 2.0 (16-way) directions:

```csharp
    private static Direction ToOutputDirection(Context context, Direction direction)
    {
        // Factorio 2.0 uses 16-way directions (N=0, E=4, S=8, W=12); internal directions are 1.1-style 8-way.
        return (Direction)((int)direction * 2);
    }
```

Change the `Version` assignment (line ~225) from the `AddHeatPipes ? ... : ...` conditional to:

```csharp
            Version = FormatVersion(2, 0, 32, 0),
```

Add a private helper near `ToOutputItems`:

```csharp
    private static string? ToOutputQuality(Quality quality)
    {
        return quality == Quality.Normal ? null : Qualities.ToBlueprintString(quality);
    }
```

Stamp quality on the three entity kinds:
- Pumpjack (in the `case PumpjackCenter` block, ~line 108): add `Quality = ToOutputQuality(context.Options.PumpjackQuality),` to the `new Entity { ... }` initializer.
- Beacon (in the `case BeaconCenter` block, ~line 182): add `Quality = ToOutputQuality(context.Options.BeaconQuality),`.
- Electric pole (in the `case ElectricPoleCenter` block, ~line 156): add `Quality = ToOutputQuality(context.Options.ElectricPoleQuality),`.

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.EmitsTwoPointZeroAndEntityQualityWithoutHeat|FullyQualifiedName~PlannerTest.OmitsQualityFieldWhenNormal"`
Expected: PASS.

- [ ] **Step 6: Run the existing version test to confirm it still holds**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.EmitsHeatPipesInTwoPointZeroBlueprint"`
Expected: PASS (major version still 2 with heat on).

- [ ] **Step 7: Commit**

```bash
git add src/FactorioTools/Data/Entity.cs src/FactorioTools.Serialization/OilField/Steps/GridToBlueprintString.cs test/FactorioTools.Test/OilField/PlannerTest.cs
git commit -m "Emit Factorio 2.0 always and stamp entity quality"
```

---

### Task 5: Module quality emission

**Files:**
- Modify: `src/FactorioTools.Serialization/Data/EntityItemsConverter.cs` (add `Quality` to `ModuleInsertPlan`; write it in the `id` object)
- Modify: `src/FactorioTools.Serialization/OilField/Steps/GridToBlueprintString.cs` (`ToOutputItems` always produces the 2.0 array; accept a quality; pass per-set quality at call sites)
- Test: `test/FactorioTools.Test/OilField/PlannerTest.cs`

**Interfaces:**
- Consumes: `ModuleInsertPlan` (existing), `Qualities.ToBlueprintString` (Task 1), `OilFieldOptions.PumpjackModuleQuality`/`.BeaconModuleQuality` (Task 2).
- Changes signature: `GridToBlueprintString.ToOutputItems(Context context, Dictionary<string,int> modules, int inventory, Quality quality)`.
- Adds: `ModuleInsertPlan.Quality` (nullable string).

- [ ] **Step 1: Write the failing test**

Add to `test/FactorioTools.Test/OilField/PlannerTest.cs`. This asserts on the raw JSON, since the parser's `EntityItemsConverter.Read` ignores the 2.0 items array. Use the contract/serialization JSON to inspect emitted module quality by decoding the blueprint string back to JSON.

```csharp
    [Fact]
    public void EmitsModuleQualityInItemsArray()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        options.PumpjackModuleQuality = Quality.Epic;
        options.BeaconModuleQuality = Quality.Legendary;
        var blueprint = ParseBlueprint.Execute(SmallListBlueprintStrings[0]);
        var (context, _) = Planner.Execute(options, blueprint);

        // Act
        var blueprintString = GridToBlueprintString.Execute(context, addFbeOffset: false, addAvoidEntities: false);
        var json = DecodeBlueprintJson(blueprintString); // helper added in Step 3

        // Assert: the emitted JSON contains the quality inside module id objects
        Assert.Contains("\"quality\":\"epic\"", json);
        Assert.Contains("\"quality\":\"legendary\"", json);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.EmitsModuleQualityInItemsArray"`
Expected: FAIL (compile error: `DecodeBlueprintJson` not defined). You will add that helper in Step 3, then it should fail on the assertion until the implementation is done.

- [ ] **Step 3: Add a JSON-decode test helper**

Blueprint strings are version byte `'0'` + base64(zlib(json)). Mirror `ParseBlueprint` (which uses `ZLibStream`). Add this helper to `BasePlannerTest` (`test/FactorioTools.Test/OilField/BasePlannerTest.cs`):

```csharp
    public static string DecodeBlueprintJson(string blueprintString)
    {
        var base64 = blueprintString.Substring(1); // strip the leading version byte '0'
        var compressed = Convert.FromBase64String(base64);
        using var input = new System.IO.MemoryStream(compressed);
        using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new System.IO.StreamReader(zlib);
        return reader.ReadToEnd();
    }
```

Re-run Step 2's command to confirm it now compiles and FAILS on the assertion (module quality not yet emitted).

- [ ] **Step 4: Add Quality to ModuleInsertPlan and write it**

In `src/FactorioTools.Serialization/Data/EntityItemsConverter.cs`:

Add to `ModuleInsertPlan`:

```csharp
    public string? Quality { get; set; }
```

In `EntityItemsConverter.Write`, inside the `if (value is List<ModuleInsertPlan> plans)` branch, where the `id` object is written:

```csharp
                writer.WritePropertyName("id");
                writer.WriteStartObject();
                writer.WriteString("name", plan.Name);
                if (plan.Quality is not null)
                {
                    writer.WriteString("quality", plan.Quality);
                }
                writer.WriteEndObject();
```

- [ ] **Step 5: Make ToOutputItems always emit the 2.0 array with quality**

In `src/FactorioTools.Serialization/OilField/Steps/GridToBlueprintString.cs`, replace `ToOutputItems` (lines ~55-76) with:

```csharp
    private static object? ToOutputItems(Context context, Dictionary<string, int> modules, int inventory, Quality quality)
    {
        if (modules is null || modules.Count == 0)
        {
            return null;
        }

        var qualityString = quality == Quality.Normal ? null : Qualities.ToBlueprintString(quality);
        var plans = new List<ModuleInsertPlan>();
        var stack = 0;
        foreach (var pair in modules)
        {
            plans.Add(new ModuleInsertPlan { Name = pair.Key, Inventory = inventory, StartStack = stack, Count = pair.Value, Quality = qualityString });
            stack += pair.Value;
        }

        return plans;
    }
```

Update the two call sites:
- Pumpjack (line ~114): `Items = ToOutputItems(context, context.Options.PumpjackModules, MiningDrillModuleInventory, context.Options.PumpjackModuleQuality),`
- Beacon (line ~187): `Items = ToOutputItems(context, context.Options.BeaconModules, BeaconModuleInventory, context.Options.BeaconModuleQuality),`

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.EmitsModuleQualityInItemsArray"`
Expected: PASS.

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS, no snapshot changes.

- [ ] **Step 8: Commit**

```bash
git add src/FactorioTools.Serialization/Data/EntityItemsConverter.cs src/FactorioTools.Serialization/OilField/Steps/GridToBlueprintString.cs test/FactorioTools.Test/OilField/PlannerTest.cs test/FactorioTools.Test/OilField/BasePlannerTest.cs
git commit -m "Emit module quality in the 2.0 items array"
```

---

### Task 6: Verify Lua-safe build, then CLI quality flags

**Files:**
- Modify: `src/FactorioTools/OilField/Planner.cs` (extract `GetSampleBlueprint()`)
- Modify: `src/FactorioTools.Cli/Program.cs` (add quality options to the `sample` command)

**Interfaces:**
- Consumes: `Quality` (Task 1), `OilFieldOptions` quality fields (Task 2).
- Produces: `Planner.GetSampleBlueprint()` returning the fixed 4-pumpjack `Blueprint` used by `ExecuteSample`.

- [ ] **Step 1: Confirm the core builds and tests under Lua settings**

Run: `dotnet test /p:UseLuaSettings=true`
Expected: PASS. (This proves the new core code - enum, Context fields, pole formula - is Lua-safe before we touch the CLI and regenerate Lua in Task 9.)

If it fails to build, fix the offending construct (replace LINQ/expression with a plain loop or `switch`) and re-run before continuing.

- [ ] **Step 2: Extract GetSampleBlueprint from ExecuteSample**

In `src/FactorioTools/OilField/Planner.cs`, `ExecuteSample()` (lines ~11-83) builds a fixed `Blueprint inputBlueprint` inline and ends with `return Execute(options, inputBlueprint);`. Refactor so the blueprint comes from a new public method, leaving behavior identical:

```csharp
    public static PlannerResult ExecuteSample()
    {
        var options = OilFieldOptions.ForMediumElectricPole;
        options.PipeStrategies = OilFieldOptions.AllPipeStrategies.ToList();
        options.BeaconStrategies = OilFieldOptions.AllBeaconStrategies.ToList();
        options.ValidateSolution = true;

        return Execute(options, GetSampleBlueprint());
    }

    public static Blueprint GetSampleBlueprint()
    {
        return new Blueprint
        {
            // ... move the existing `inputBlueprint` initializer here verbatim (the 4 pumpjack entities,
            //     Icons, and Item) ...
        };
    }
```

Move the entire existing `new Blueprint { ... }` initializer (entities, icons, item) into `GetSampleBlueprint`. Do not change any values.

- [ ] **Step 3: Verify the sample still works after the refactor**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.ExecuteSample"`
Expected: PASS (the `ExecuteSample` verified snapshot is unchanged).

- [ ] **Step 4: Add quality flags to the CLI sample command**

The CLI `sample` command currently is:

```csharp
        sampleCommand.SetHandler(() =>
        {
            var (context, summary) = Planner.ExecuteSample();
            Console.WriteLine(context.Grid.ToString());
        });
```

Replace it with five `Option<Quality>` flags applied to sample options (System.CommandLine binds enum values by name, e.g. `Legendary`):

```csharp
        var pumpjackQualityOption = new Option<Quality>("--pumpjack-quality", () => Quality.Normal, "Pumpjack entity quality");
        var beaconQualityOption = new Option<Quality>("--beacon-quality", () => Quality.Normal, "Beacon entity quality");
        var electricPoleQualityOption = new Option<Quality>("--electric-pole-quality", () => Quality.Normal, "Electric pole entity quality");
        var pumpjackModuleQualityOption = new Option<Quality>("--pumpjack-module-quality", () => Quality.Normal, "Pumpjack module quality");
        var beaconModuleQualityOption = new Option<Quality>("--beacon-module-quality", () => Quality.Normal, "Beacon module quality");
        sampleCommand.AddOption(pumpjackQualityOption);
        sampleCommand.AddOption(beaconQualityOption);
        sampleCommand.AddOption(electricPoleQualityOption);
        sampleCommand.AddOption(pumpjackModuleQualityOption);
        sampleCommand.AddOption(beaconModuleQualityOption);

        sampleCommand.SetHandler((InvocationContext invocationContext) =>
        {
            var options = OilFieldOptions.ForMediumElectricPole;
            options.PipeStrategies = OilFieldOptions.AllPipeStrategies.ToList();
            options.BeaconStrategies = OilFieldOptions.AllBeaconStrategies.ToList();
            options.ValidateSolution = true;
            options.PumpjackQuality = invocationContext.ParseResult.GetValueForOption(pumpjackQualityOption);
            options.BeaconQuality = invocationContext.ParseResult.GetValueForOption(beaconQualityOption);
            options.ElectricPoleQuality = invocationContext.ParseResult.GetValueForOption(electricPoleQualityOption);
            options.PumpjackModuleQuality = invocationContext.ParseResult.GetValueForOption(pumpjackModuleQualityOption);
            options.BeaconModuleQuality = invocationContext.ParseResult.GetValueForOption(beaconModuleQualityOption);

            var (context, summary) = Planner.Execute(options, Planner.GetSampleBlueprint());
            Console.WriteLine(context.Grid.ToString());
        });
```

Add `using System.CommandLine.Invocation;` at the top of `Program.cs` if `InvocationContext` is not already in scope. The `SetHandler` overload taking an `InvocationContext` is built into System.CommandLine.

- [ ] **Step 5: Run the CLI to verify it works**

Run: `dotnet run --project src/FactorioTools.Cli -- oil-field sample --electric-pole-quality Legendary`
Expected: prints the planner grid without error.
Run: `dotnet run --project src/FactorioTools.Cli -- oil-field sample`
Expected: prints the planner grid (unchanged from before).

- [ ] **Step 6: Commit**

```bash
git add src/FactorioTools/OilField/Planner.cs src/FactorioTools.Cli/Program.cs
git commit -m "Add quality flags to the CLI sample command"
```

---

### Task 7: Regenerate the swagger client and add the Quality TS enum helper

**Files:**
- Regenerate: `src/WebApp/swagger.json` (via WebApp build)
- Regenerate: `src/vue/src/lib/FactorioToolsApi.ts` (via `npm run swagger-gen`)
- Create: `src/vue/src/lib/quality.ts` (quality metadata: order, level, label, color)
- Test: `src/vue/src/lib/quality.test.ts`

**Interfaces:**
- Consumes: the regenerated `Quality` enum from `FactorioToolsApi.ts` (string-valued: `Quality.Legendary = "Legendary"`).
- Produces: `qualityLevel(q: Quality): number` (0/1/2/3/5), `QUALITY_ORDER: Quality[]`, `qualityLabel(q)`, `qualityColor(q)` in `src/vue/src/lib/quality.ts`.

- [ ] **Step 1: Regenerate swagger.json**

Run: `dotnet build src/WebApp/WebApp.csproj`
Expected: build succeeds; the `PostBuild` target rewrites `src/WebApp/swagger.json`. Confirm the file now contains a `Quality` schema and the five new request properties:
Run: `grep -c "Quality" src/WebApp/swagger.json`
Expected: a non-zero count, and `grep "\"Quality\"" src/WebApp/swagger.json` shows an enum schema with string values `Normal`/`Uncommon`/`Rare`/`Epic`/`Legendary`.

If `Quality` appears as an integer enum rather than a string enum, ensure the WebApp's JSON options include a `JsonStringEnumConverter` (check `src/WebApp/Program.cs` AddJsonOptions). It already applies one to all enums per the existing setup, so this should be string-valued automatically.

- [ ] **Step 2: Regenerate the TS client**

Run (from `src/vue`): `npm run swagger-gen`
Expected: `src/vue/src/lib/FactorioToolsApi.ts` now contains `export enum Quality { Normal = "Normal", Uncommon = "Uncommon", Rare = "Rare", Epic = "Epic", Legendary = "Legendary" }` and the five quality fields on the request interfaces.

- [ ] **Step 3: Write the failing test**

Create `src/vue/src/lib/quality.test.ts`:

```ts
import { describe, expect, it } from "vitest"
import { Quality } from "./FactorioToolsApi"
import { qualityLevel, QUALITY_ORDER } from "./quality"

describe("quality", () => {
  it("maps quality to its bonus level", () => {
    expect(qualityLevel(Quality.Normal)).toBe(0)
    expect(qualityLevel(Quality.Uncommon)).toBe(1)
    expect(qualityLevel(Quality.Rare)).toBe(2)
    expect(qualityLevel(Quality.Epic)).toBe(3)
    expect(qualityLevel(Quality.Legendary)).toBe(5)
  })

  it("orders qualities from normal to legendary", () => {
    expect(QUALITY_ORDER).toEqual([
      Quality.Normal,
      Quality.Uncommon,
      Quality.Rare,
      Quality.Epic,
      Quality.Legendary,
    ])
  })
})
```

- [ ] **Step 4: Run the test to verify it fails**

Run (from `src/vue`): `npx vitest run src/lib/quality.test.ts`
Expected: FAIL (cannot resolve `./quality`).

- [ ] **Step 5: Create the quality metadata module**

Create `src/vue/src/lib/quality.ts`:

```ts
import { Quality } from "./FactorioToolsApi"

export const QUALITY_ORDER: Quality[] = [
  Quality.Normal,
  Quality.Uncommon,
  Quality.Rare,
  Quality.Epic,
  Quality.Legendary,
]

const levels: Record<Quality, number> = {
  [Quality.Normal]: 0,
  [Quality.Uncommon]: 1,
  [Quality.Rare]: 2,
  [Quality.Epic]: 3,
  [Quality.Legendary]: 5,
}

const labels: Record<Quality, string> = {
  [Quality.Normal]: "Normal",
  [Quality.Uncommon]: "Uncommon",
  [Quality.Rare]: "Rare",
  [Quality.Epic]: "Epic",
  [Quality.Legendary]: "Legendary",
}

// Official Factorio quality colors (approximate; confirm against the game/wiki).
const colors: Record<Quality, string> = {
  [Quality.Normal]: "#c8c8c8",
  [Quality.Uncommon]: "#4fd24f",
  [Quality.Rare]: "#3f9bff",
  [Quality.Epic]: "#b34dff",
  [Quality.Legendary]: "#ff912d",
}

export function qualityLevel(quality: Quality): number {
  return levels[quality]
}

export function qualityLabel(quality: Quality): string {
  return labels[quality]
}

export function qualityColor(quality: Quality): string {
  return colors[quality]
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run (from `src/vue`): `npx vitest run src/lib/quality.test.ts`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/WebApp/swagger.json src/vue/src/lib/FactorioToolsApi.ts src/vue/src/lib/quality.ts src/vue/src/lib/quality.test.ts
git commit -m "Regenerate API client with Quality and add quality TS metadata"
```

---

### Task 8: Vue UI - quality selectors, store, and request wiring

**Files:**
- Create: `src/vue/src/components/QualitySelect.vue`
- Modify: `src/vue/src/stores/OilFieldStore.ts` (defaults + storeToQuery keys)
- Modify: `src/vue/src/lib/OilFieldPlanner.ts` (request getters)
- Modify: `src/vue/src/components/PumpjacksForm.vue`, `BeaconForm.vue`, `ElectricPoleForm.vue`
- Test: `src/vue/src/lib/OilFieldPlanner.test.ts` (extend the existing tests)

**Interfaces:**
- Consumes: `Quality`, `qualityLevel`, `qualityLabel`, `qualityColor`, `QUALITY_ORDER` (Task 7); the store state shape (Task 8 additions).
- Produces: store fields `pumpjackQuality`, `beaconQuality`, `electricPoleQuality`, `pumpjackModuleQuality`, `beaconModuleQuality` (string-typed, default `"Normal"`); matching request getters `pumpjackQuality`, `beaconQuality`, `electricPoleQuality`, `pumpjackModuleQuality`, `beaconModuleQuality`.

- [ ] **Step 1: Write the failing test for the request getters**

Open `src/vue/src/lib/OilFieldPlanner.test.ts` and find how it builds a state and asserts on the request (it tests `requestPropertyGetters` or `buildPlanRequest`). Add a test that mirrors the existing style; if the file exposes `buildPlanRequest`/`requestPropertyGetters`, use it. Example (adapt the import/setup to the existing test's helpers):

```ts
it("passes quality fields through to the request", () => {
  const state = makeState({
    pumpjackQuality: "Legendary",
    beaconQuality: "Rare",
    electricPoleQuality: "Uncommon",
    pumpjackModuleQuality: "Epic",
    beaconModuleQuality: "Legendary",
  })
  const request = buildPlanRequest(state)
  expect(request.pumpjackQuality).toBe("Legendary")
  expect(request.beaconQuality).toBe("Rare")
  expect(request.electricPoleQuality).toBe("Uncommon")
  expect(request.pumpjackModuleQuality).toBe("Epic")
  expect(request.beaconModuleQuality).toBe("Legendary")
})
```

If `buildPlanRequest`/`makeState` are not exported, follow the file's existing pattern for constructing state and invoking the request builder (read the top of `OilFieldPlanner.test.ts` first and match it exactly).

- [ ] **Step 2: Run the test to verify it fails**

Run (from `src/vue`): `npx vitest run src/lib/OilFieldPlanner.test.ts`
Expected: FAIL (request lacks the quality fields).

- [ ] **Step 3: Add store fields and query keys**

In `src/vue/src/stores/OilFieldStore.ts`:

Add to the `defaults` object (alongside the other pumpjack/beacon/pole settings):

```ts
  pumpjackQuality: "Normal",
  pumpjackModuleQuality: "Normal",
  beaconQuality: "Normal",
  beaconModuleQuality: "Normal",
  electricPoleQuality: "Normal",
```

Add to the `storeToQuery` map (every state key must have a query key or the type fails to compile):

```ts
  pumpjackQuality: "pumpQ",
  pumpjackModuleQuality: "pumpModQ",
  beaconQuality: "beaconQ",
  beaconModuleQuality: "beaconModQ",
  electricPoleQuality: "poleQ",
```

- [ ] **Step 4: Add request getters**

In `src/vue/src/lib/OilFieldPlanner.ts`, add to `requestPropertyGetters` (the values are the raw quality strings, which match the generated `Quality` enum values):

```ts
  pumpjackQuality: (state) => state.pumpjackQuality as Quality,
  pumpjackModuleQuality: (state) => state.pumpjackModuleQuality as Quality,
  beaconQuality: (state) => state.beaconQuality as Quality,
  beaconModuleQuality: (state) => state.beaconModuleQuality as Quality,
  electricPoleQuality: (state) => state.electricPoleQuality as Quality,
```

Add `Quality` to the existing import from `./FactorioToolsApi` at the top of the file.

- [ ] **Step 5: Run the getter test to verify it passes**

Run (from `src/vue`): `npx vitest run src/lib/OilFieldPlanner.test.ts`
Expected: PASS.

- [ ] **Step 6: Create the QualitySelect component**

Create `src/vue/src/components/QualitySelect.vue`. It is a labeled `<select>` of the five qualities, each option prefixed with a small colored badge, shown only under advanced options:

```vue
<template>
  <div class="row" v-show="showAdvancedOptions">
    <div class="col">
      <label :for="idPrefix + '-quality'" class="form-label d-flex align-items-center gap-2">
        <span class="quality-badge" :style="{ backgroundColor: badgeColor }"></span>
        {{ label }}
      </label>
      <select
        class="form-select"
        :id="idPrefix + '-quality'"
        :value="modelValue"
        @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value)"
      >
        <option v-for="q in order" :key="q" :value="q">{{ qualityLabel(q) }}</option>
      </select>
    </div>
  </div>
</template>

<script lang="ts">
import { Quality } from "../lib/FactorioToolsApi"
import { QUALITY_ORDER, qualityColor, qualityLabel } from "../lib/quality"

export default {
  props: {
    showAdvancedOptions: { type: Boolean, required: true },
    label: { type: String, required: true },
    idPrefix: { type: String, required: true },
    modelValue: { type: String, required: true },
  },
  emits: ["update:modelValue"],
  computed: {
    order() {
      return QUALITY_ORDER
    },
    badgeColor(): string {
      return qualityColor(this.modelValue as Quality)
    },
  },
  methods: {
    qualityLabel,
  },
}
</script>

<style scoped>
.quality-badge {
  display: inline-block;
  width: 0.85rem;
  height: 0.85rem;
  border-radius: 2px;
  border: 1px solid rgba(0, 0, 0, 0.35);
  /* A chevron-like notch evokes the in-game quality pip without bundling game art. */
  clip-path: polygon(50% 0, 100% 50%, 50% 100%, 0 50%);
}
</style>
```

- [ ] **Step 7: Wire QualitySelect into the three forms**

In `src/vue/src/components/PumpjacksForm.vue`:
- Import `QualitySelect` and register it in `components`.
- Add `"pumpjackQuality"`, `"pumpjackModuleQuality"` to the `pick(...)` call in `data()`.
- In the template, after the existing `<ModuleSelect ... />`, add:

```vue
    <QualitySelect
      label="Pumpjack quality"
      idPrefix="pumpjack"
      :showAdvancedOptions="showAdvancedOptions"
      v-model="pumpjackQuality"
    />
    <QualitySelect
      label="Pumpjack module quality"
      idPrefix="pumpjack-module"
      :showAdvancedOptions="showAdvancedOptions"
      v-model="pumpjackModuleQuality"
    />
```

In `src/vue/src/components/BeaconForm.vue`:
- Import/register `QualitySelect`; add `"beaconQuality"`, `"beaconModuleQuality"` to the `pick(...)` list in `data()`.
- In the template, inside the `v-show="showAdvancedOptions && addBeacons"` advanced block (or just after the `<ModuleSelect>`), add two `<QualitySelect>` entries bound to `beaconQuality` (label "Beacon quality", idPrefix "beacon") and `beaconModuleQuality` (label "Beacon module quality", idPrefix "beacon-module"), each passing `:showAdvancedOptions="showAdvancedOptions"`.

In `src/vue/src/components/ElectricPoleForm.vue`:
- Import/register `QualitySelect`; add `"electricPoleQuality"` to the `pick(...)` list.
- Import `qualityLevel` from `../lib/quality`.
- In the template, inside the `v-show="showAdvancedOptions && addElectricPoles"` block, add a `<QualitySelect label="Electric pole quality" idPrefix="electric-pole" :showAdvancedOptions="showAdvancedOptions" v-model="electricPoleQuality" />` followed by an effective-coverage hint:

```vue
    <p class="form-text" v-show="showAdvancedOptions && addElectricPoles">
      Effective coverage at {{ electricPoleQuality }}:
      supply {{ Number(electricPoleSupplyWidth) + 2 * level }}x{{ Number(electricPoleSupplyHeight) + 2 * level }},
      wire reach {{ Number(electricPoleWireReach) + 2 * level }}
    </p>
```

Add a computed `level()` returning `qualityLevel(this.electricPoleQuality as Quality)` (import `Quality` from `../lib/FactorioToolsApi`).

- [ ] **Step 8: Type-check and build the front-end**

Run (from `src/vue`): `npm run build`
Expected: `swagger-gen` + `vue-tsc` + `vite build` succeed with no type errors. (This also re-confirms the generated client and the store/getter types line up.)

- [ ] **Step 9: Run the front-end tests**

Run (from `src/vue`): `npx vitest run`
Expected: PASS (existing tests plus the new quality and getter tests).

- [ ] **Step 10: Rebuild the WASM bundle**

Run (from `src/vue`): `npm run build-wasm`
Expected: republishes `BrowserWasm` and refreshes `public/framework` (so the deployed planner includes quality). If no local .NET 10 SDK / wasm-tools workload is available, use `./docker-build.sh` per CLAUDE.md and copy the `_framework` bundle into `src/vue/public/framework`.

- [ ] **Step 11: Manually verify in the browser (optional but recommended)**

Run (from `src/vue`): `npm run preview` (after `npm run build`), open the app, enable advanced options, set electric pole quality to Legendary, plan the sample, and confirm: the effective-coverage hint updates, the plan succeeds, and the output blueprint contains `"quality":"legendary"` (paste into a text decoder or import into Factorio). Note: `npm run dev` cannot run the WASM planner; use build + preview.

- [ ] **Step 12: Commit**

```bash
git add src/vue/src
git commit -m "Add quality selectors to the Vue UI"
```

---

### Task 9: Regenerate the transpiled Lua

**Files:**
- Regenerate: `src/lua/**` (via `src/lua/Invoke-LuaBuild.ps1`)

**Interfaces:**
- Consumes: all core changes (Tasks 1-5) that are transpiled (the enum, Context fields, pole formula, emission). The Lua build mirrors the C# core and serialization libraries.

- [ ] **Step 1: Regenerate the Lua**

Run: `pwsh src/lua/Invoke-LuaBuild.ps1`
Expected: regenerates the `src/lua` output from the C# source. If `pwsh` is not installed, install PowerShell (`brew install --cask powershell`) or run the script on a machine that has it; the CSharp.lua submodule must be present (`git submodule update --init --recursive`).

- [ ] **Step 2: Syntax-check the generated Lua**

Run (fish): `for f in src/lua/**/*.lua; luac5.2 -p $f; end`
Expected: no output (all files parse). If `luac5.2` is not installed, install it (`brew install lua@5.2` or equivalent) - `luac5.2`/`lua5.2` only validate syntax, not Factorio runtime APIs.

- [ ] **Step 3: Spot-check the quality output in Lua**

Run: `grep -rn "ElectricPoleWireReachSquaredWithQuality\|ToBlueprintString\|quality" src/lua | head`
Expected: the regenerated Lua references the new Context field and the quality mapping, confirming the transpile picked up the core changes.

- [ ] **Step 4: Commit**

```bash
git add src/lua
git commit -m "Regenerate transpiled Lua with quality support"
```

---

### Task 10: Full verification

**Files:** none (verification only).

- [ ] **Step 1: Run the full .NET test suite (default config)**

Run: `dotnet test`
Expected: PASS, no changed `*.verified.txt` snapshots.

- [ ] **Step 2: Run the full .NET test suite under Lua settings**

Run: `dotnet test /p:UseLuaSettings=true`
Expected: PASS.

- [ ] **Step 3: Run the front-end test suite and build**

Run (from `src/vue`): `npx vitest run; and npm run build`
Expected: PASS and a clean build.

- [ ] **Step 4: Confirm the working tree is clean except intended files**

Run: `git status`
Expected: only the committed changes; no stray modified snapshots or generated files left uncommitted.

- [ ] **Step 5: Final commit (only if anything remains)**

```bash
git add -A
git commit -m "Finalize quality support"
```

(If `git status` is already clean, skip this step.)

---

## Self-review notes

- Spec section "Data model" -> Tasks 1, 2. "Electric-pole quality and planner geometry" -> Task 3. "Blueprint emission" -> Tasks 4 (version/direction/entity quality) and 5 (module quality). "Vue UI" -> Tasks 7, 8. "CLI" -> Task 6. "Lua transpile parity" -> Tasks 6 (Lua-safe build gate) and 9 (regenerate). "Testing" -> tests embedded in each task plus Task 10.
- Pole formula `base + 2 * level` and level mapping (`Legendary = 5`) are used consistently in Tasks 1, 3, 7, and the ElectricPoleForm hint in Task 8.
- `Quality` is serialized by name over the wire (Task 1 converter) and mapped to lowercase only at emission (Tasks 4, 5) - kept distinct everywhere.
- Big-pole scaling row and exact quality hex colors are the two facts to confirm during implementation (Tasks 3 and 7 respectively).
