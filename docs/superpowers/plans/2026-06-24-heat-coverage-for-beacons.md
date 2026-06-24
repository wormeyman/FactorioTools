# Heat Coverage for Beacons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the Aquilo heat network to cover beacons, drop beacons that cannot be heated, and move browser planning into a Web Worker so long runs do not freeze the page.

**Architecture:** The core library keeps routing heat per pipe layout (pipes + pumpjacks) and planning beacons around it; plan selection is unchanged and ranks on pre-drop beacon effect counts (fast path). All new core work happens once on the winning plan in `AddPipes.Execute`: after beacons are placed, a new `AddHeatPipes.ExtendToBeacons` grows the existing connected heat network to reach beacon footprints, then unheatable beacons are removed and the reported counts updated. The Vue front-end runs the WASM planner in a single dedicated Web Worker with a cancel path.

**Tech Stack:** C# (.NET 10), xUnit + Verify snapshots, CSharp.lua transpilation (Lua 5.2), Vue 3 + Vite + TypeScript, .NET WASM.

## Global Constraints

- **.NET SDK 10.0.100** pinned in `global.json`. If no local .NET 10 SDK, build/test via `./docker-build.sh`.
- **Lua-safe core:** in `AddHeatPipes.cs` and other core hot paths, no LINQ, `yield return`, named tuples, try/catch, or struct dictionary keys. Reuse `ILocationSet` / `ILocationDictionary` containers and deterministic iteration.
- **Build under both flag sets:** core data-structure changes must compile and test green under default **and** `/p:UseLuaSettings=true`.
- **Verify snapshots:** when behavior changes, update `*.verified.txt` via the Verify accept workflow (received vs verified), never by hand.
- **Text style:** use hyphens (`-`), never em/en dashes, in all code and docs.
- **Commit frequently:** one commit per task.
- Spec: `docs/superpowers/specs/2026-06-24-heat-coverage-for-beacons-design.md`.

## File Structure

Core library:
- `src/FactorioTools/OilField/Models/BeaconSolution.cs` - add per-beacon effect counts.
- `src/FactorioTools/OilField/Steps/PlanBeacons.0.cs` - thread per-beacon counts through.
- `src/FactorioTools/OilField/Steps/PlanBeacons.1.FBE.cs` - emit per-beacon counts.
- `src/FactorioTools/OilField/Steps/PlanBeacons.2.Snug.cs` - emit per-beacon counts.
- `src/FactorioTools/OilField/Steps/AddHeatPipes.cs` - extract `GrowFrom`, add `ExtendToBeacons`.
- `src/FactorioTools/OilField/Steps/AddPipes.0.cs` - extend + drop + stat update in `Execute`.
- `src/FactorioTools/OilField/Steps/Validate.cs` - `NoUnheatedBeacons` assertion.
- `src/FactorioTools/OilField/Planner.cs` - call the new validation.

Tests:
- `test/FactorioTools.Test/OilField/PlannerTest.cs` - new integration test(s).
- `test/FactorioTools.Test/OilField/*.verified.txt`, `Score.HasExpectedScore.verified.txt` - snapshot updates.

Front-end (`src/vue`):
- `src/vue/src/lib/planner.worker.ts` - new Web Worker hosting the WASM runtime.
- `src/vue/src/lib/wasmPlanner.ts` - thin worker client + `PlanCancelledError` + `cancel()`.
- `src/vue/src/lib/OilFieldPlanner.ts` - rethrow `PlanCancelledError`.
- `src/vue/src/views/OilField.vue` - cancel button, info note, store refs, cancel handling.

`BeaconPlannerResult` lives in `src/FactorioTools/OilField/Steps/PlanBeacons.0.cs` (top of file).

---

### Task 1: Carry per-beacon effect counts

Both beacon planners already add a beacon's effect contribution in lockstep with adding the beacon. Chosen beacons are either independently counted (overlap on) or disjoint (overlap off), so summing survivors' contributions recomputes total effects exactly after a drop. This task makes those per-beacon counts available on `BeaconSolution`.

**Files:**
- Modify: `src/FactorioTools/OilField/Steps/PlanBeacons.0.cs` (`BeaconPlannerResult` record at line 6, deconstruction at lines 37-47)
- Modify: `src/FactorioTools/OilField/Models/BeaconSolution.cs:5`
- Modify: `src/FactorioTools/OilField/Steps/PlanBeacons.1.FBE.cs` (`GetBeacons`, lines 64-105)
- Modify: `src/FactorioTools/OilField/Steps/PlanBeacons.2.Snug.cs` (`Execute`, lines 41-127)

**Interfaces:**
- Produces: `BeaconSolution(BeaconStrategy Strategy, List<Location> Beacons, int Effects, List<int> EffectsGivenCounts)` where `EffectsGivenCounts[i]` is the effect contribution of `Beacons[i]` and `EffectsGivenCounts.Count == Beacons.Count`.
- Produces: `BeaconPlannerResult(List<Location> Beacons, int Effects, List<int> EffectsGivenCounts)`.

- [ ] **Step 1: Find every construction site of the two records**

Run: `grep -rn "new BeaconPlannerResult\|new BeaconSolution\|BeaconPlannerResult(\|BeaconSolution(" src test`
Expected: `BeaconPlannerResult` returned in `PlanBeacons.1.FBE.cs` and `PlanBeacons.2.Snug.cs`; `BeaconSolution` constructed in `PlanBeacons.0.cs`. Note any others (e.g. tests) and update them in this task.

- [ ] **Step 2: Add the field to `BeaconPlannerResult`**

In `src/FactorioTools/OilField/Steps/PlanBeacons.0.cs`, change line 6:

```csharp
public record BeaconPlannerResult(List<Location> Beacons, int Effects, List<int> EffectsGivenCounts);
```

- [ ] **Step 3: Add the field to `BeaconSolution`**

In `src/FactorioTools/OilField/Models/BeaconSolution.cs`, change line 5:

```csharp
public record BeaconSolution(BeaconStrategy Strategy, List<Location> Beacons, int Effects, List<int> EffectsGivenCounts);
```

- [ ] **Step 4: Emit per-beacon counts in the FBE planner**

In `src/FactorioTools/OilField/Steps/PlanBeacons.1.FBE.cs`, in `GetBeacons` (lines 64-105), add the list and populate it next to the effect sum:

```csharp
    private static BeaconPlannerResult GetBeacons(Context context, List<Area> effectEntityAreas, List<BeaconCandidate> possibleBeacons)
    {
        var beacons = new List<Location>();
        var effectsGivenCounts = new List<int>();
        var effects = 0;
```

and inside the loop, immediately after `effects += beacon.EffectsGivenCount;` (line 99):

```csharp
            effects += beacon.EffectsGivenCount;
            effectsGivenCounts.Add(beacon.EffectsGivenCount);
```

and change the return (line 104):

```csharp
        return new BeaconPlannerResult(beacons, effects, effectsGivenCounts);
```

- [ ] **Step 5: Emit per-beacon counts in the Snug planner**

In `src/FactorioTools/OilField/Steps/PlanBeacons.2.Snug.cs`, in `Execute`, add the list next to `var effects = 0;` (line 42):

```csharp
        var beacons = new List<Location>();
        var effectsGivenCounts = new List<int>();
        var effects = 0;
```

and inside the inner loop, immediately after `effects += info.CoveredCount;` (line 97):

```csharp
                effects += info.CoveredCount;
                effectsGivenCounts.Add(info.CoveredCount);
```

and change the return (line 126):

```csharp
        return new BeaconPlannerResult(beacons, effects, effectsGivenCounts);
```

- [ ] **Step 6: Thread the counts through `PlanBeacons.Execute`**

In `src/FactorioTools/OilField/Steps/PlanBeacons.0.cs`, change the deconstruction (line 37) and the solution construction (line 47):

```csharp
            (var beacons, var effects, var effectsGivenCounts) = strategy switch
            {
                BeaconStrategy.FbeOriginal => PlanBeaconsFbe.Execute(context, strategy),
                BeaconStrategy.Fbe => PlanBeaconsFbe.Execute(context, strategy),
                BeaconStrategy.Snug => PlanBeaconsSnug.Execute(context),
                _ => throw new NotImplementedException(),
            };

            completedStrategies[(int)strategy] = true;

            solutions.Add(new BeaconSolution(strategy, beacons, effects, effectsGivenCounts));
```

- [ ] **Step 7: Fix any other construction sites found in Step 1**

For each remaining `new BeaconSolution(...)` / `new BeaconPlannerResult(...)` (e.g. in tests), pass the matching per-beacon list. If a test only needs a placeholder, pass an empty `new List<int>()` only when `Beacons` is also empty; otherwise build the parallel list.

- [ ] **Step 8: Build and run the full test suite**

Run: `dotnet build` then `dotnet test`
Expected: PASS. Effect totals are unchanged, so all existing snapshots stay green. (The new field is exercised by Task 3.)

- [ ] **Step 9: Commit**

```bash
git add src/FactorioTools/OilField/Models/BeaconSolution.cs src/FactorioTools/OilField/Steps/PlanBeacons.0.cs src/FactorioTools/OilField/Steps/PlanBeacons.1.FBE.cs src/FactorioTools/OilField/Steps/PlanBeacons.2.Snug.cs
git commit -m "Carry per-beacon effect counts on BeaconSolution"
```

---

### Task 2: Extract a pre-seeded grow helper

`AddHeatPipes.Grow` both picks a seed and runs the grow loop. The beacon extension needs the grow loop seeded from the existing network instead. Extract the loop so both callers share it. This is a pure refactor - behavior must not change.

**Files:**
- Modify: `src/FactorioTools/OilField/Steps/AddHeatPipes.cs` (`Grow`, lines 162-277)

**Interfaces:**
- Produces: `private static void GrowFrom(Context context, ILocationSet chosen, List<Location> candidates, ILocationDictionary<List<Location>> coveredPipes, ILocationDictionary<List<Location>> coveredCenters, ILocationSet uncoveredPipes, ILocationSet uncoveredCenters)` - grows the already-seeded `chosen` network until all reachable uncovered targets are covered.

- [ ] **Step 1: Add `GrowFrom` containing the existing grow loop**

In `src/FactorioTools/OilField/Steps/AddHeatPipes.cs`, add this method (the body is the current `Grow` loop from `var unreachable = ...` at line 213 through the end of the `while` at line 276):

```csharp
    /// <summary>
    /// Grows an already-seeded connected network (<paramref name="chosen"/> must be non-empty) until every reachable
    /// uncovered target is covered, bridging through empty tiles when no adjacent candidate makes progress.
    /// </summary>
    private static void GrowFrom(
        Context context,
        ILocationSet chosen,
        List<Location> candidates,
        ILocationDictionary<List<Location>> coveredPipes,
        ILocationDictionary<List<Location>> coveredCenters,
        ILocationSet uncoveredPipes,
        ILocationSet uncoveredCenters)
    {
#if USE_STACKALLOC && LOCATION_AS_STRUCT
        Span<Location> adjacent = stackalloc Location[4];
#else
        Span<Location> adjacent = new Location[4];
#endif

        // Candidates we could not reach by bridging; skip them on later passes to avoid looping.
        var unreachable = context.GetLocationSet();

        while (uncoveredPipes.Count > 0 || uncoveredCenters.Count > 0)
        {
            // Fast path: the best candidate orthogonally adjacent to the current network (keeps it connected for free).
            var bestAdjacent = Location.Invalid;
            var bestAdjacentGain = 0;

            // Bridge fallback: the best candidate anywhere that still covers something and we have not failed to reach.
            var bestAny = Location.Invalid;
            var bestAnyGain = 0;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (chosen.Contains(candidate))
                {
                    continue;
                }

                var gain = Gain(coveredPipes, coveredCenters, candidate, uncoveredPipes, uncoveredCenters);
                if (gain == 0)
                {
                    continue;
                }

                if (!unreachable.Contains(candidate) && gain > bestAnyGain)
                {
                    bestAnyGain = gain;
                    bestAny = candidate;
                }

                if (gain > bestAdjacentGain && IsAdjacentToNetwork(grid: context.Grid, chosen, candidate, adjacent))
                {
                    bestAdjacentGain = gain;
                    bestAdjacent = candidate;
                }
            }

            if (bestAdjacentGain > 0)
            {
                AddTile(coveredPipes, coveredCenters, chosen, bestAdjacent, uncoveredPipes, uncoveredCenters);
                continue;
            }

            if (bestAnyGain == 0)
            {
                // Remaining targets have no empty candidate tile at all - they are boxed in and cannot be heated.
                break;
            }

            // Bridge the network out to the best remaining candidate.
            var path = BridgeToTile(context, chosen, bestAny);
            if (path is null)
            {
                unreachable.Add(bestAny);
                continue;
            }

            for (var i = 0; i < path.Count; i++)
            {
                AddTile(coveredPipes, coveredCenters, chosen, path[i], uncoveredPipes, uncoveredCenters);
            }
        }
    }
```

- [ ] **Step 2: Replace the loop in `Grow` with a call to `GrowFrom`**

In `Grow`, delete the body from `var unreachable = context.GetLocationSet();` (line 213) through the closing brace of the `while` loop (line 276), and replace it with the seed call already present plus:

```csharp
        AddTile(coveredPipes, coveredCenters, chosen, seed, uncoveredPipes, uncoveredCenters);

        GrowFrom(context, chosen, candidates, coveredPipes, coveredCenters, uncoveredPipes, uncoveredCenters);
    }
```

(The seed-selection block above `AddTile(... seed ...)` stays unchanged.)

- [ ] **Step 3: Build and run the heat tests**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest"`
Expected: PASS, including `AddsHeatPipesForAquilo`, `AddsHeatPipesAndBeaconsTogetherForAquilo`, `HeatOnlyPrefersHeatableLayoutAcrossSmallList`, `DropsPumpjacksToFullyHeatBoxedInField`. Behavior is unchanged by the refactor.

- [ ] **Step 4: Commit**

```bash
git add src/FactorioTools/OilField/Steps/AddHeatPipes.cs
git commit -m "Extract GrowFrom from AddHeatPipes.Grow (no behavior change)"
```

---

### Task 3: Extend heat to beacons and drop the unheatable ones

**Files:**
- Modify: `src/FactorioTools/OilField/Steps/AddHeatPipes.cs` (new `ExtendToBeacons`)
- Modify: `src/FactorioTools/OilField/Steps/AddPipes.0.cs` (`Execute`, lines 51-55)
- Test: `test/FactorioTools.Test/OilField/PlannerTest.cs`

**Interfaces:**
- Consumes: `GrowFrom` (Task 2); `BeaconSolution.EffectsGivenCounts` (Task 1); `Helpers.RemoveEntity(SquareGrid grid, Location center, int width, int height)`; `Helpers.AddBeaconsToGrid`.
- Produces: `public static ILocationSet ExtendToBeacons(Context context, IReadOnlyList<Location> beaconCenters)` - grows `context.HeatPipes` to reach beacon footprints, places the new heat tiles, unions them into `context.HeatPipes`, and returns the set of beacon center locations that ended up heat-adjacent.

- [ ] **Step 1: Write the failing integration test**

In `test/FactorioTools.Test/OilField/PlannerTest.cs`, add (near `AddsHeatPipesAndBeaconsTogetherForAquilo`):

```csharp
    [Theory]
    [MemberData(nameof(SmallListBlueprintIndexes))]
    public void HeatsEveryKeptBeaconWhenBeaconsAndHeatAreOn(int index)
    {
        // On Aquilo an unheated beacon freezes and gives no effects, so every beacon left in the
        // output must have an adjacent heat pipe; unheatable beacons must be dropped, not kept.
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        options.AddHeatPipes = true;
        options.AddBeacons = true;
        var result = Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[index]));
        var grid = result.Context.Grid;

        var width = options.BeaconWidth;
        var height = options.BeaconHeight;

        foreach (var location in grid.EntityLocations.EnumerateItems())
        {
            if (grid[location] is not BeaconCenter)
            {
                continue;
            }

            var minX = location.X - ((width - 1) / 2);
            var maxX = location.X + (width / 2);
            var minY = location.Y - ((height - 1) / 2);
            var maxY = location.Y + (height / 2);

            var heated = false;
            for (var x = minX; x <= maxX && !heated; x++)
            {
                for (var y = minY; y <= maxY && !heated; y++)
                {
                    foreach (var n in new[]
                    {
                        new Location(x - 1, y), new Location(x + 1, y),
                        new Location(x, y - 1), new Location(x, y + 1),
                    })
                    {
                        var insideFootprint = n.X >= minX && n.X <= maxX && n.Y >= minY && n.Y <= maxY;
                        if (!insideFootprint && grid.IsInBounds(n) && grid[n] is HeatPipe)
                        {
                            heated = true;
                            break;
                        }
                    }
                }
            }

            Assert.True(heated, $"beacon at {location} (index {index}) is not heat-adjacent");
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~HeatsEveryKeptBeaconWhenBeaconsAndHeatAreOn"`
Expected: FAIL on at least one index with "beacon at ... is not heat-adjacent" (today beacons are not heated).

- [ ] **Step 3: Implement `ExtendToBeacons`**

In `src/FactorioTools/OilField/Steps/AddHeatPipes.cs`, add:

```csharp
    /// <summary>
    /// Extends the already-placed heat network (<see cref="Context.HeatPipes"/>) to reach the placed beacons. A beacon
    /// is heated when a heat pipe is orthogonally adjacent to its footprint. The footprint is derived from
    /// <see cref="OilFieldOptions.BeaconWidth"/> / <see cref="OilFieldOptions.BeaconHeight"/> so custom beacon sizes
    /// work. New heat tiles are placed on the grid and added to <see cref="Context.HeatPipes"/>. Returns the set of
    /// beacon center locations that ended up heated; beacons not in the set are boxed in and cannot be heated.
    /// </summary>
    public static ILocationSet ExtendToBeacons(Context context, IReadOnlyList<Location> beaconCenters)
    {
        var grid = context.Grid;
        var heatedBeacons = context.GetLocationSet(allowEnumerate: true);

        if (context.HeatPipes is null || beaconCenters.Count == 0)
        {
            return heatedBeacons;
        }

        var width = context.Options.BeaconWidth;
        var height = context.Options.BeaconHeight;

        var uncoveredBeacons = context.GetLocationSet(allowEnumerate: true);
        for (var i = 0; i < beaconCenters.Count; i++)
        {
            uncoveredBeacons.Add(beaconCenters[i]);
        }

        var candidates = new List<Location>();
        var coveredBeacons = context.GetLocationDictionary<List<Location>>();

        // Empty placeholders for the pipe coverage channel that GrowFrom shares with the pumpjack/pipe router.
        var noPipeCoverage = context.GetLocationDictionary<List<Location>>();
        var noPipes = context.GetLocationSet(allowEnumerate: true);

#if USE_STACKALLOC && LOCATION_AS_STRUCT
        Span<Location> adjacent = stackalloc Location[4];
#else
        Span<Location> adjacent = new Location[4];
#endif

        for (var b = 0; b < beaconCenters.Count; b++)
        {
            var center = beaconCenters[b];
            var minX = center.X - ((width - 1) / 2);
            var maxX = center.X + (width / 2);
            var minY = center.Y - ((height - 1) / 2);
            var maxY = center.Y + (height / 2);

            // First pass: is this beacon already adjacent to the existing network?
            var alreadyHeated = false;
            for (var x = minX; x <= maxX && !alreadyHeated; x++)
            {
                for (var y = minY; y <= maxY && !alreadyHeated; y++)
                {
                    grid.GetAdjacent(adjacent, new Location(x, y));
                    for (var i = 0; i < adjacent.Length; i++)
                    {
                        var n = adjacent[i];
                        if (n.IsValid && grid[n] is HeatPipe)
                        {
                            alreadyHeated = true;
                            break;
                        }
                    }
                }
            }

            if (alreadyHeated)
            {
                uncoveredBeacons.Remove(center);
                continue;
            }

            // Second pass: register empty footprint-adjacent tiles as candidates that would heat this beacon.
            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    grid.GetAdjacent(adjacent, new Location(x, y));
                    for (var i = 0; i < adjacent.Length; i++)
                    {
                        var n = adjacent[i];
                        if (!n.IsValid)
                        {
                            continue;
                        }

                        var insideFootprint = n.X >= minX && n.X <= maxX && n.Y >= minY && n.Y <= maxY;
                        if (!insideFootprint && grid.IsEmpty(n))
                        {
                            AddCoverage(coveredBeacons, candidates, n, center);
                        }
                    }
                }
            }
        }

        // Seed the grow with the entire existing network so the extension stays one connected component.
        var chosen = context.GetLocationSet(allowEnumerate: true);
        chosen.UnionWith(context.HeatPipes);

        if (candidates.Count > 0 && uncoveredBeacons.Count > 0)
        {
            GrowFrom(context, chosen, candidates, noPipeCoverage, coveredBeacons, noPipes, uncoveredBeacons);
        }

        // Place the newly chosen tiles (chosen minus the pre-existing network) and grow the network set.
        foreach (var location in chosen.EnumerateItems())
        {
            if (context.HeatPipes.Contains(location))
            {
                continue;
            }

            grid.AddEntity(location, new HeatPipe(grid.GetId()));
            context.HeatPipes.Add(location);
        }

        for (var i = 0; i < beaconCenters.Count; i++)
        {
            if (!uncoveredBeacons.Contains(beaconCenters[i]))
            {
                heatedBeacons.Add(beaconCenters[i]);
            }
        }

        return heatedBeacons;
    }
```

- [ ] **Step 4: Wire the extension and drop into `AddPipes.Execute`**

In `src/FactorioTools/OilField/Steps/AddPipes.0.cs`, replace the beacon-placement block (lines 51-55):

```csharp
        if (bestBeacons is not null)
        {
            AddBeaconsToGrid(context.Grid, context.Options, bestBeacons.Beacons);

            // On Aquilo, extend the heat network to reach the placed beacons, then drop any beacon that still cannot be
            // heated (an unheated beacon freezes and gives no effects). Selection already ran on the pre-drop effect
            // counts (fast path); update the reported counts so the summary matches the blueprint.
            if (context.Options.AddHeatPipes && context.HeatPipes is not null)
            {
                var heatedBeacons = AddHeatPipes.ExtendToBeacons(context, bestBeacons.Beacons);

                var keptEffects = 0;
                var keptCount = 0;
                for (var i = 0; i < bestBeacons.Beacons.Count; i++)
                {
                    var center = bestBeacons.Beacons[i];
                    if (heatedBeacons.Contains(center))
                    {
                        keptEffects += bestBeacons.EffectsGivenCounts[i];
                        keptCount++;
                    }
                    else
                    {
                        RemoveEntity(context.Grid, center, context.Options.BeaconWidth, context.Options.BeaconHeight);
                    }
                }

                if (selectedPlans.Count > 0)
                {
                    selectedPlans[0] = selectedPlans[0] with
                    {
                        BeaconEffectCount = keptEffects,
                        BeaconCount = keptCount,
                    };
                }
            }
        }
```

- [ ] **Step 5: Run the new test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~HeatsEveryKeptBeaconWhenBeaconsAndHeatAreOn"`
Expected: PASS for all indexes.

- [ ] **Step 6: Commit**

```bash
git add src/FactorioTools/OilField/Steps/AddHeatPipes.cs src/FactorioTools/OilField/Steps/AddPipes.0.cs test/FactorioTools.Test/OilField/PlannerTest.cs
git commit -m "Extend heat to beacons and drop unheatable beacons on Aquilo"
```

---

### Task 4: Validate that no kept beacon is unheated

A correctness guard so a regression in the drop logic fails loudly rather than emitting a frozen beacon.

**Files:**
- Modify: `src/FactorioTools/OilField/Steps/Validate.cs` (after `CountUnheatedTargets`, line 215)
- Modify: `src/FactorioTools/OilField/Planner.cs` (after line 225)
- Test: `test/FactorioTools.Test/OilField/PlannerTest.cs`

**Interfaces:**
- Produces: `public static void NoUnheatedBeacons(Context context)` - throws `FactorioToolsException` if `ValidateSolution` is on, heat and beacons are both enabled, and any `BeaconCenter` footprint lacks an adjacent heat pipe.

- [ ] **Step 1: Write the failing test**

In `test/FactorioTools.Test/OilField/PlannerTest.cs`, add:

```csharp
    [Fact]
    public void ValidatesBeaconsAreHeatedWhenValidationIsOn()
    {
        // With validation on, planning heat + beacons across the small list must never throw - every kept
        // beacon is heat-adjacent and the unheatable ones are dropped before validation runs.
        for (var index = 0; index < SmallListBlueprintStrings.Count; index++)
        {
            var options = OilFieldOptions.ForMediumElectricPole;
            options.ValidateSolution = true;
            options.AddHeatPipes = true;
            options.AddBeacons = true;

            var ex = Record.Exception(() => Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[index])));
            Assert.Null(ex);
        }
    }
```

- [ ] **Step 2: Run the test to confirm it passes today, then make the validation enforce it**

Run: `dotnet test --filter "FullyQualifiedName~ValidatesBeaconsAreHeatedWhenValidationIsOn"`
Expected: PASS (Task 3 already heats or drops beacons). This test pins the behavior; the validation below makes the invariant explicit and self-checking.

- [ ] **Step 3: Add `NoUnheatedBeacons` to `Validate`**

In `src/FactorioTools/OilField/Steps/Validate.cs`, add after `CountUnheatedTargets` (line 215):

```csharp
    public static void NoUnheatedBeacons(Context context)
    {
        if (!context.Options.ValidateSolution
            || !context.Options.AddHeatPipes
            || !context.Options.AddBeacons
            || context.HeatPipes is null)
        {
            return;
        }

        var grid = context.Grid;
        var width = context.Options.BeaconWidth;
        var height = context.Options.BeaconHeight;

#if USE_STACKALLOC && LOCATION_AS_STRUCT
        Span<Location> adjacent = stackalloc Location[4];
#else
        Span<Location> adjacent = new Location[4];
#endif

        foreach (var location in grid.EntityLocations.EnumerateItems())
        {
            if (grid[location] is not BeaconCenter)
            {
                continue;
            }

            var minX = location.X - ((width - 1) / 2);
            var maxX = location.X + (width / 2);
            var minY = location.Y - ((height - 1) / 2);
            var maxY = location.Y + (height / 2);

            var heated = false;
            for (var x = minX; x <= maxX && !heated; x++)
            {
                for (var y = minY; y <= maxY && !heated; y++)
                {
                    grid.GetAdjacent(adjacent, new Location(x, y));
                    for (var i = 0; i < adjacent.Length; i++)
                    {
                        var n = adjacent[i];
                        if (n.IsValid && grid[n] is HeatPipe)
                        {
                            heated = true;
                            break;
                        }
                    }
                }
            }

            if (!heated)
            {
                throw new FactorioToolsException("A beacon was left without an adjacent heat pipe on Aquilo, so it would freeze.");
            }
        }
    }
```

- [ ] **Step 4: Call the validation in `Planner`**

In `src/FactorioTools/OilField/Planner.cs`, after line 225 (`Validate.HeatPipesAreConnected(context);`):

```csharp
        Validate.HeatPipesAreConnected(context);
        Validate.NoUnheatedBeacons(context);
```

- [ ] **Step 5: Run the test and the heat suite**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest"`
Expected: PASS, including the new test and `HeatsEveryKeptBeaconWhenBeaconsAndHeatAreOn`.

- [ ] **Step 6: Commit**

```bash
git add src/FactorioTools/OilField/Steps/Validate.cs src/FactorioTools/OilField/Planner.cs test/FactorioTools.Test/OilField/PlannerTest.cs
git commit -m "Validate every kept beacon is heated on Aquilo"
```

---

### Task 5: Update Verify snapshots and the scoreboard

Heat + beacon combos now drop unheatable beacons and extend heat, so grid snapshots and the score scoreboard shift.

**Files:**
- Modify (regenerate): `test/FactorioTools.Test/OilField/PlannerTest.AddsHeatPipesAndBeaconsTogetherForAquilo.verified.txt`, `Score.HasExpectedScore.verified.txt`, and any other `*.verified.txt` the run reports as changed.

- [ ] **Step 1: Run the full suite to surface snapshot diffs**

Run: `dotnet test`
Expected: FAILs limited to Verify snapshot mismatches (`*.received.txt` produced). Confirm each diff reflects intended changes (beacons heat-adjacent, unheatable beacons removed, score deltas) and is not an unintended regression. The `EnablingBeaconsNeverForcesMoreHeatDrops` invariant must still pass.

- [ ] **Step 2: Accept the snapshots**

Review each `*.received.txt` against its `*.verified.txt`, then accept. From `test/FactorioTools.Test`:

```bash
for f in (find . -name "*.received.txt"); mv $f (string replace ".received.txt" ".verified.txt" $f); end
```

(fish syntax). Only do this after confirming the diffs are correct.

- [ ] **Step 3: Re-run to confirm green**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add test/FactorioTools.Test/OilField
git commit -m "Update snapshots and scoreboard for heated beacons"
```

---

### Task 6: Confirm Lua-safe build

**Files:** none (build verification).

- [ ] **Step 1: Build the core library under Lua settings**

Run: `dotnet build src/FactorioTools/FactorioTools.csproj /p:UseLuaSettings=true`
Expected: PASS with no errors. (If no local .NET 10 SDK: `./docker-build.sh build src/FactorioTools/FactorioTools.csproj /p:UseLuaSettings=true`.)

- [ ] **Step 2: Run the test suite under Lua settings**

Run: `dotnet test /p:UseLuaSettings=true`
Expected: PASS. Confirms the new `ExtendToBeacons` / `GrowFrom` / `NoUnheatedBeacons` paths use only Lua-safe constructs and containers.

- [ ] **Step 3: Commit (if any fixes were required)**

```bash
git add -A
git commit -m "Keep heat-on-beacons paths Lua-safe"
```

(Skip if Steps 1-2 passed with no changes.)

---

### Task 7: Run WASM planning in a Web Worker

Move the WASM runtime off the main thread so the existing Plan-button spinner animates and runs can be cancelled.

**Files:**
- Create: `src/vue/src/lib/planner.worker.ts`
- Modify (rewrite): `src/vue/src/lib/wasmPlanner.ts`

**Interfaces:**
- Consumes: `__BASE_PATH__` global define (already used by `wasmPlanner.ts`).
- Produces: `wasmPlanner.plan(requestJson: string): Promise<string>`, `wasmPlanner.normalize(requestJson: string): Promise<string>`, `wasmPlanner.cancel(): void`, and `class PlanCancelledError extends Error`.

- [ ] **Step 1: Create the worker**

Create `src/vue/src/lib/planner.worker.ts`:

```ts
// Runs the .NET WASM planner off the main thread. Boots the runtime once, then answers
// plan/normalize messages posted by wasmPlanner.ts. A single, non-threaded dedicated worker -
// no SharedArrayBuffer and no cross-origin-isolation headers required.

declare const self: DedicatedWorkerGlobalScope
export {}

type Exports = {
  Interop: {
    Plan: (requestJson: string) => string
    Normalize: (requestJson: string) => string
  }
}

let exportsPromise: Promise<Exports> | null = null

function bootExports(): Promise<Exports> {
  if (!exportsPromise) {
    exportsPromise = (async () => {
      const dotnetUrl = `${__BASE_PATH__}framework/dotnet.js`
      const { dotnet } = await import(/* @vite-ignore */ dotnetUrl)
      const { getAssemblyExports, getConfig } = await dotnet.withDiagnosticTracing(false).create()
      const config = getConfig()
      return (await getAssemblyExports(config.mainAssemblyName)) as Exports
    })()
  }
  return exportsPromise
}

self.onmessage = async (e: MessageEvent) => {
  const { id, op, requestJson } = e.data as {
    id: number
    op: "plan" | "normalize"
    requestJson: string
  }
  try {
    const exports = await bootExports()
    const responseJson =
      op === "plan" ? exports.Interop.Plan(requestJson) : exports.Interop.Normalize(requestJson)
    self.postMessage({ id, responseJson })
  } catch (err) {
    self.postMessage({
      id,
      error: err instanceof Error ? (err.stack ?? err.message) : String(err),
    })
  }
}
```

- [ ] **Step 2: Rewrite `wasmPlanner.ts` as a worker client**

Replace the contents of `src/vue/src/lib/wasmPlanner.ts`:

```ts
// Thin main-thread client for the planner Web Worker (planner.worker.ts). Keeps the same
// plan/normalize Promise interface OilFieldPlanner.ts expects, and adds cancel().

export class PlanCancelledError extends Error {
  constructor() {
    super("Planning was cancelled.")
    this.name = "PlanCancelledError"
  }
}

type PendingRequest = {
  resolve: (responseJson: string) => void
  reject: (error: unknown) => void
}

let worker: Worker | null = null
let nextId = 0
const pending = new Map<number, PendingRequest>()

function getWorker(): Worker {
  if (!worker) {
    worker = new Worker(new URL("./planner.worker.ts", import.meta.url), { type: "module" })
    worker.onmessage = (e: MessageEvent) => {
      const { id, responseJson, error } = e.data as {
        id: number
        responseJson?: string
        error?: string
      }
      const req = pending.get(id)
      if (!req) {
        return
      }
      pending.delete(id)
      if (error !== undefined) {
        req.reject(new Error(error))
      } else {
        req.resolve(responseJson as string)
      }
    }
    worker.onerror = (e) => {
      const err = new Error(e.message || "The planner worker failed.")
      for (const [, req] of pending) {
        req.reject(err)
      }
      pending.clear()
    }
  }
  return worker
}

function invoke(op: "plan" | "normalize", requestJson: string): Promise<string> {
  const w = getWorker()
  const id = nextId++
  return new Promise<string>((resolve, reject) => {
    pending.set(id, { resolve, reject })
    w.postMessage({ id, op, requestJson })
  })
}

export function plan(requestJson: string): Promise<string> {
  return invoke("plan", requestJson)
}

export function normalize(requestJson: string): Promise<string> {
  return invoke("normalize", requestJson)
}

export function cancel(): void {
  if (worker) {
    worker.terminate()
    worker = null
  }
  for (const [, req] of pending) {
    req.reject(new PlanCancelledError())
  }
  pending.clear()
}
```

- [ ] **Step 3: Type-check and build the front-end**

Run (from `src/vue`): `npm run build`
Expected: PASS (type-check and Vite build succeed; the worker bundles via the `new URL(..., import.meta.url)` form). If `DedicatedWorkerGlobalScope` is unknown, confirm `vite/client` / `webworker` libs are in `tsconfig`; the `declare const self` line keeps the worker self-contained.

- [ ] **Step 4: Commit**

```bash
git add src/vue/src/lib/planner.worker.ts src/vue/src/lib/wasmPlanner.ts
git commit -m "Run WASM planner in a Web Worker with cancel support"
```

---

### Task 8: Cancel button, info note, and cancel handling in the UI

**Files:**
- Modify: `src/vue/src/lib/OilFieldPlanner.ts` (`runWasm`, lines 90-124)
- Modify: `src/vue/src/views/OilField.vue` (template around lines 100-115; script data lines 215-220, methods around `invokeApi` lines 377-391)

**Interfaces:**
- Consumes: `PlanCancelledError`, `cancel` from `wasmPlanner.ts` (Task 7).

- [ ] **Step 1: Rethrow `PlanCancelledError` from `runWasm`**

In `src/vue/src/lib/OilFieldPlanner.ts`, import the error at the top (with the other imports):

```ts
import * as wasmPlanner from "./wasmPlanner"
import { PlanCancelledError } from "./wasmPlanner"
```

and in the `catch` of `runWasm` (lines 117-124), let cancellation propagate instead of becoming a generic error:

```ts
  } catch (e) {
    if (e instanceof PlanCancelledError) {
      throw e
    }
    return {
      isError: true,
      title: "An unexpected error occurred.",
      errorDetails: [e instanceof Error ? (e.stack ?? e.toString()) : JSON.stringify(e)],
    }
  }
```

- [ ] **Step 2: Add the cancel button and info note to the template**

In `src/vue/src/views/OilField.vue`, replace the Plan-button block (lines 100-115) with:

```html
    <div v-if="addHeatPipes && addBeacons" class="alert alert-info mt-3" role="alert">
      Heating beacons runs entirely in your browser. Large oil fields can take a while to plan -
      you can cancel a run in progress.
    </div>
    <div class="d-grid gap-2">
      <button
        type="submit"
        class="btn btn-primary btn-lg"
        @click.prevent="submit"
        :disabled="cannotSubmit"
      >
        <span
          v-if="submitting"
          class="spinner-border spinner-border-sm"
          role="status"
          aria-hidden="true"
        ></span>
        Plan oil field
      </button>
      <button
        v-if="submitting"
        type="button"
        class="btn btn-outline-secondary"
        @click.prevent="cancel"
      >
        Cancel
      </button>
    </div>
```

- [ ] **Step 3: Expose `addHeatPipes` / `addBeacons` to the component**

In `src/vue/src/views/OilField.vue`, add the two store refs to the `pick(...)` call in `data()` (lines 215-220):

```ts
      pick(
        storeToRefs(useOilFieldStore()),
        "usingQueryString",
        "useAdvancedOptions",
        "inputBlueprint",
        "addHeatPipes",
        "addBeacons",
      ),
```

- [ ] **Step 4: Import the cancel helpers and handle cancellation**

In `src/vue/src/views/OilField.vue` script, add to the imports:

```ts
import { cancel as cancelPlanning, PlanCancelledError } from "../lib/wasmPlanner"
```

Add a `cancel` method (in `methods`, next to `submit`):

```ts
    cancel() {
      cancelPlanning()
    },
```

Update `invokeApi` (lines 377-391) to swallow cancellation rather than surfacing it as an error:

```ts
    async invokeApi<Data>(api: () => Promise<ApiResult<Data> | ApiError>) {
      if (this.cannotSubmit) {
        return
      }

      this.submitting = true
      // A small non-zero delay lets the browser paint the loading state before work begins.
      await new Promise((r) => setTimeout(r, 10))
      try {
        await api()
      } catch (e) {
        if (!(e instanceof PlanCancelledError)) {
          throw e
        }
        // Cancelled by the user - leave the previous plan/errors untouched.
      } finally {
        this.submitting = false
      }
    },
```

- [ ] **Step 5: Build the front-end**

Run (from `src/vue`): `npm run build`
Expected: PASS (type-check + build).

- [ ] **Step 6: Commit**

```bash
git add src/vue/src/lib/OilFieldPlanner.ts src/vue/src/views/OilField.vue
git commit -m "Add cancel button and heat+beacons notice to the planner UI"
```

---

### Task 9: Manual end-to-end verification

**Files:** none (manual verification).

- [ ] **Step 1: Refresh the WASM bundle from the core changes**

Run (from `src/vue`): `npm run build-wasm`
Expected: publishes `BrowserWasm` and copies `_framework` into `src/vue/public/framework` (incl. `dotnet.js`). If no local .NET 10 SDK + wasm-tools, follow the CLAUDE.md Docker path and copy the bundle manually.

- [ ] **Step 2: Run the dev server and exercise the flow**

Run (from `src/vue`): `npm run dev`
Then in the browser:
- Enable Heat pipes + Beacons; confirm the blue info note appears.
- Paste a large blueprint and click Plan oil field; confirm the spinner animates (page does not freeze) and a Cancel button appears.
- Click Cancel mid-run; confirm planning stops, no error is shown, and a subsequent Plan still works (worker re-boots).
- Complete a plan; confirm the output blueprint contains heat pipes and that beacons present in the output are adjacent to heat pipes (spot check via the View in FBE link).

- [ ] **Step 3: Final confirmation**

Run: `dotnet test` (full suite, default flags)
Expected: PASS. No commit needed unless the bundle refresh changed tracked files.

---

## Self-Review

**Spec coverage:**
- Goal 1 (heat beacons): Tasks 2-3 (`GrowFrom`, `ExtendToBeacons`, wiring). Covered.
- Goal 2 (drop unheatable, only working beacons in output): Task 3 drop loop + Task 4 validation. Covered.
- Goal 3 (off main thread, responsive): Tasks 7-8 (worker, client, cancel, spinner, note). Covered.
- Selection unchanged / fast path: Task 3 keeps `GetSolution` untouched, updates only `selectedPlans[0]` post-drop. Covered.
- Per-beacon effect recompute: Task 1 plumbing + Task 3 sum-of-survivors. Covered (exact in both overlap modes).
- Validation invariant: Task 4. Covered.
- Snapshots + scoreboard: Task 5. Covered.
- Lua safety / dual-flag build: Task 6, plus Global Constraints. Covered.
- UI info note with hyphens: Task 8 Step 2. Covered.

**Placeholder scan:** No TBD/TODO; every code step shows complete code; commands have expected output.

**Type consistency:** `BeaconPlannerResult` and `BeaconSolution` both gain `List<int> EffectsGivenCounts` (Task 1) and are consumed by index in Task 3. `GrowFrom` signature defined in Task 2 matches its call in Task 3's `ExtendToBeacons`. `ExtendToBeacons(Context, IReadOnlyList<Location>)` defined in Task 3 matches its call in `AddPipes.Execute`. `PlanCancelledError` / `cancel` exported in Task 7 match imports in Task 8. `Helpers.RemoveEntity(grid, center, width, height)` matches the signature at `Helpers.cs:897`.
