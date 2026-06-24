# Heat-driven pumpjack drop + beacon/heat coexistence guard removal - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When heat pipes are enabled, always output a fully heated, fully connected field - ranking pipe layouts by heat coverage and dropping the fewest pumpjacks needed when no layout heats the full set - and remove the now-obsolete Vue guard that forbids beacons + heat together.

**Architecture:** The heat router already routes per pipe layout and reports coverage. We (1) stop discarding heat-infeasible layouts and instead rank by coverage, (2) wrap solution selection in a minimal-drop replan loop that removes one pumpjack at a time until the selected layout is fully heatable, (3) make heat-coverage validation non-throwing and report drop/residual counts on the plan summary, and (4) surface a UI warning and delete the temporary mutual-exclusion guard.

**Tech Stack:** C# (.NET 8, `Knapcode.FactorioTools` core, no serialization deps), xUnit + Verify snapshots, Vue 3 + Pinia + TypeScript, Swashbuckle-generated `swagger.json` -> `swagger-typescript-api` client.

## Global Constraints

- Use hyphens (`-`), never em/en dashes, in all files.
- Every planner behavior change is gated on `context.Options.AddHeatPipes`; non-heat plans must be byte-for-byte unchanged (`Score.HasExpectedScore.verified.txt` must not change).
- Core lib stays Lua-safe: no `yield return`, LINQ, named tuples, try/catch, or struct dictionary keys in hot paths; drop-candidate selection must be deterministic (tie-break on `(Y, X)`) because Factorio modifies `pairs()` / `math.random()`.
- Build AND test under default settings and under `/p:UseLuaSettings=true`.
- .NET SDK 8.0.100 (pinned in `global.json`). If no local .NET 8 SDK, use `./docker-build.sh`.
- Tests use Verify: when a snapshot legitimately changes, accept via the received/verified workflow, never hand-edit `*.verified.txt`.

---

## File Structure

**C# core (`src/FactorioTools`):**
- `OilField/Steps/AddHeatPipes.cs` - `Route` / `RouteCore` report the still-uncovered pipe and pumpjack-center sets (not just a bool).
- `OilField/Steps/AddPipes.0.cs` - `Solution` stores uncovered sets; `GetSolution` records them; `GetAllPlans` drops the infeasible-filter; the sort comparator ranks by heat coverage when heat is on; new `SelectBestSolution` wraps the drop loop; new `ChooseDropCandidate` / `DropPumpjack`.
- `OilField/Helpers.cs` - new `RemovePumpjack` mirroring `AddPumpjack`.
- `OilField/Models/Context.cs` - new `HeatDroppedPumpjacks` int.
- `OilField/Steps/Validate.cs` - `HeatPipesCoverAllTargets` (throwing) -> `CountUnheatedTargets` (non-throwing counts); `HeatPipesAreConnected` unchanged.
- `OilField/Planner.cs` - exclude heat drops from the `missingPumpjacks` throw; populate new summary fields.
- `OilField/Models/OilFieldPlanSummary.cs` - new `HeatDroppedPumpjacks`, `UnheatedPumpjacks`, `UnheatedPipes`.

**Tests (`test/FactorioTools.Test/OilField`):**
- `PlannerTest.cs` - rework two try/catch heat tests, adjust one, add a boxed-in-field drop test + Verify snapshot.

**Generated client + Vue (`src/vue`, `src/WebApp`):**
- `src/WebApp/swagger.json` + `src/vue/src/lib/FactorioToolsApi.ts` - regenerated.
- `src/vue/src/components/OilFieldPlanView.vue` - heat warning alert.
- `src/vue/src/stores/OilFieldStore.ts` - delete the guard.
- `src/vue/src/components/BeaconForm.vue` - delete the conflict-warning block + import.

---

## Task 1: Allow and measure partial heat (remove the hard fail)

Stops the planner from discarding heat-infeasible layouts and from throwing on partial coverage; ranks by coverage and reports the gap. After this task a boxed-in field with heat on no longer errors - it returns the most-heatable layout (still partial; the drop loop comes in Task 2).

**Files:**
- Modify: `src/FactorioTools/OilField/Steps/AddHeatPipes.cs` (`Route` ~68-86, `RouteCore` ~92-155, `Execute` ~50)
- Modify: `src/FactorioTools/OilField/Steps/AddPipes.0.cs` (`Solution` ~529-542, `GetSolution` ~424-454, `GetAllPlans` ~184-244, comparator ~76-126)
- Modify: `src/FactorioTools/OilField/Models/Context.cs` (add field)
- Modify: `src/FactorioTools/OilField/Steps/Validate.cs` (`HeatPipesCoverAllTargets` ~164-217)
- Modify: `src/FactorioTools/OilField/Models/OilFieldPlanSummary.cs`
- Modify: `src/FactorioTools/OilField/Planner.cs` (`missingPumpjacks` ~197-201, summary ~227-234)
- Test: `test/FactorioTools.Test/OilField/PlannerTest.cs`

**Interfaces:**
- Produces:
  - `AddHeatPipes.Route(Context context, ILocationSet pipeTiles, out ILocationSet uncoveredPipes, out ILocationSet uncoveredCenters)` returning the chosen heat `ILocationSet`.
  - `Solution.UncoveredPipes` / `Solution.UncoveredCenters` (`ILocationSet`), plus existing `HeatFeasible` (now derived: both counts 0).
  - `Context.HeatDroppedPumpjacks` (`int`, default 0).
  - `Validate.CountUnheatedTargets(Context context, out int unheatedPumpjacks, out int unheatedPipes)` (non-throwing).
  - `OilFieldPlanSummary` extended with `int HeatDroppedPumpjacks, int UnheatedPumpjacks, int UnheatedPipes` (added after `RotatedPumpjacks`).

- [ ] **Step 1: Write the failing test**

Add to `test/FactorioTools.Test/OilField/PlannerTest.cs`. The index is a placeholder you will resolve in Step 2; for now assert the behavior on a field known to be boxed-in.

```csharp
[Fact]
public void HeatOnPartialFieldReportsGapInsteadOfThrowing()
{
    // A boxed-in field has no fully-heatable pipe layout. Before this change the planner threw
    // "At least one pipe strategy must be used."; now it returns the most-heatable layout and
    // reports the unheated gap on the summary. (Task 2 drives the gap to zero via dropping.)
    var options = OilFieldOptions.ForMediumElectricPole;
    options.AddHeatPipes = true;
    options.AddBeacons = false;
    // ValidateSolution stays false here: we are asserting the production path that does not throw.

    var index = BoxedInHeatIndex;
    var (_, summary) = Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[index]));

    Assert.True(summary.UnheatedPumpjacks + summary.UnheatedPipes > 0, "expected a boxed-in field to report an unheated gap");
}
```

Add a constant near the top of the class (resolved in Step 2):

```csharp
// A small-list blueprint with no fully-heatable pipe layout (every candidate layout leaves a boxed-in tile).
private const int BoxedInHeatIndex = 55;
```

- [ ] **Step 2: Find a genuinely boxed-in index**

Run a throwaway probe to pick an index whose heat-only plan is still partial after ranking. Easiest: temporarily run the existing suite after Steps 3-9 and inspect; but to choose up front, use the prior spec's evidence (`docs/superpowers/specs/2026-06-23-heat-router-coverage-incremental-design.md` cites index 55 as a dense field with buried pipes). Keep `BoxedInHeatIndex = 55`. If Step 9 shows index 55 is actually fully heatable in your build, replace it with any index for which `HeatOnlyPrefersHeatableLayoutAcrossSmallList` (Task 2) does not reach zero drops, and re-run.

Run: `dotnet build`
Expected: compiles (test references new summary fields you add below; if you wrote the test first it will not compile yet - that is the expected red state until Steps 3-8 land).

- [ ] **Step 3: Heat router reports uncovered sets (`AddHeatPipes.cs`)**

Change `RouteCore` to out the uncovered sets instead of a bool. Replace its signature and the `coversAllTargets` out-assignment:

```csharp
private static ILocationSet RouteCore(Context context, ILocationSet pipeTiles, out ILocationSet uncoveredPipesOut, out ILocationSet uncoveredCentersOut)
{
    // ... unchanged body through Grow(...) ...

    uncoveredPipesOut = uncoveredPipes;
    uncoveredCentersOut = uncoveredCenters;

    return chosen;
}
```

Change `Route` to forward them:

```csharp
public static ILocationSet Route(Context context, ILocationSet pipeTiles, out ILocationSet uncoveredPipes, out ILocationSet uncoveredCenters)
{
    var grid = context.Grid;

    foreach (var pipe in pipeTiles.EnumerateItems())
    {
        grid.AddEntity(pipe, new TemporaryEntity(grid.GetId()));
    }

    var chosen = RouteCore(context, pipeTiles, out uncoveredPipes, out uncoveredCenters);

    foreach (var pipe in pipeTiles.EnumerateItems())
    {
        grid.RemoveEntity(pipe);
    }

    return chosen;
}
```

Update `Execute` (the no-beacon production heat placement) to discard the out sets:

```csharp
var chosen = RouteCore(context, pipeTiles, out _, out _);
```

- [ ] **Step 4: `Solution` stores the uncovered sets (`AddPipes.0.cs`)**

In the `Solution` class add:

```csharp
public required ILocationSet UncoveredPipes { get; set; }
public required ILocationSet UncoveredCenters { get; set; }
```

In `GetSolution`, replace the heat-routing block and the `HeatFeasible` assignment:

```csharp
ILocationSet? heatPipes = null;
ILocationSet uncoveredPipes = EmptyLocationSet.Instance;
ILocationSet uncoveredCenters = EmptyLocationSet.Instance;
var heatFeasible = true;
if (context.Options.AddHeatPipes)
{
    heatPipes = AddHeatPipes.Route(context, optimizedPipes, out uncoveredPipes, out uncoveredCenters);
    heatFeasible = uncoveredPipes.Count == 0 && uncoveredCenters.Count == 0;
}

List<BeaconSolution>? beaconSolutions = null;
if (context.Options.AddBeacons && heatFeasible)
{
    beaconSolutions = PlanBeacons.Execute(context, optimizedPipes, heatPipes);
}
```

And in the returned `new Solution { ... }` add:

```csharp
HeatPipes = heatPipes,
HeatFeasible = heatFeasible,
UncoveredPipes = uncoveredPipes,
UncoveredCenters = uncoveredCenters,
```

(`EmptyLocationSet.Instance` is the existing shared empty set used elsewhere in this file.)

- [ ] **Step 5: Rank by coverage; remove the infeasible drop (`AddPipes.0.cs`)**

In `GetAllPlans`, delete the `requireHeatFeasible` filter. Remove these lines:

```csharp
var requireHeatFeasible = context.Options.AddHeatPipes;
```
and inside the loop:
```csharp
if (requireHeatFeasible && !solution.HeatFeasible)
{
    continue;
}
```

In the `sortedPlans.Sort(...)` comparator (in `GetBestSolution`), add heat coverage as the **first** key, before the beacon-effect comparison:

```csharp
sortedPlans.Sort((a, b) =>
{
    // When heat is on it is the hard constraint: fewer unheated targets = better, ahead of everything else.
    if (context.Options.AddHeatPipes)
    {
        var aUnheated = a.Pipes.UncoveredPipes.Count + a.Pipes.UncoveredCenters.Count;
        var bUnheated = b.Pipes.UncoveredPipes.Count + b.Pipes.UncoveredCenters.Count;
        var heat = aUnheated.CompareTo(bUnheated);
        if (heat != 0)
        {
            return heat;
        }
    }

    // more effects = better
    var c = b.Plan.BeaconEffectCount.CompareTo(a.Plan.BeaconEffectCount);
    // ... rest unchanged ...
```

(`PlanInfo.Pipes` is the `Solution`; it is already in scope in the comparator.)

- [ ] **Step 6: Add `Context.HeatDroppedPumpjacks` (`Context.cs`)**

After the `HeatPipes` property:

```csharp
/// <summary>
/// The number of pumpjacks dropped (removed from the field) to make the remaining set fully heatable on Aquilo.
/// Zero unless <see cref="OilFieldOptions.AddHeatPipes"/> is set and the full set had no fully-heatable layout.
/// </summary>
public int HeatDroppedPumpjacks { get; set; }
```

- [ ] **Step 7: `Validate.HeatPipesCoverAllTargets` -> non-throwing `CountUnheatedTargets` (`Validate.cs`)**

Replace the whole `HeatPipesCoverAllTargets` method with a counting version that never throws:

```csharp
public static void CountUnheatedTargets(Context context, out int unheatedPumpjacks, out int unheatedPipes)
{
    unheatedPumpjacks = 0;
    unheatedPipes = 0;

    if (!context.Options.AddHeatPipes || context.HeatPipes is null)
    {
        return;
    }

#if USE_STACKALLOC && LOCATION_AS_STRUCT
    Span<Location> adjacent = stackalloc Location[4];
#else
    Span<Location> adjacent = new Location[4];
#endif

    foreach (var location in context.Grid.EntityLocations.EnumerateItems())
    {
        if (context.Grid[location] is not Pipe)
        {
            continue;
        }

        context.Grid.GetAdjacent(adjacent, location);
        var heated = false;
        for (var i = 0; i < adjacent.Length && !heated; i++)
        {
            heated = adjacent[i].IsValid && context.Grid[adjacent[i]] is HeatPipe;
        }

        if (!heated)
        {
            unheatedPipes++;
        }
    }

    for (var c = 0; c < context.Centers.Count; c++)
    {
        var center = context.Centers[c];
        var heated = false;
        for (var i = 0; i < AddHeatPipes.PumpjackRingOffsets.Length && !heated; i++)
        {
            var ringLocation = center.Translate(AddHeatPipes.PumpjackRingOffsets[i]);
            heated = context.Grid.IsInBounds(ringLocation) && context.Grid[ringLocation] is HeatPipe;
        }

        if (!heated)
        {
            unheatedPumpjacks++;
        }
    }
}
```

Leave `HeatPipesAreConnected` unchanged (still throwing).

- [ ] **Step 8: Extend the summary and populate it (`OilFieldPlanSummary.cs`, `Planner.cs`)**

In `OilFieldPlanSummary.cs`, add the three params after `RotatedPumpjacks` (and matching doc-comments):

```csharp
/// <param name="HeatDroppedPumpjacks">Pumpjacks dropped so the rest of the field could be fully heated on Aquilo. Zero unless heat pipes are enabled.</param>
/// <param name="UnheatedPumpjacks">Pumpjacks still left without an adjacent heat pipe in the final output (normally zero).</param>
/// <param name="UnheatedPipes">Pipe tiles still left without an adjacent heat pipe in the final output (normally zero).</param>
public record OilFieldPlanSummary(
    int MissingPumpjacks,
    int RotatedPumpjacks,
    int HeatDroppedPumpjacks,
    int UnheatedPumpjacks,
    int UnheatedPipes,
    IReadOnlyList<OilFieldPlan> SelectedPlans,
    IReadOnlyList<OilFieldPlan> AlternatePlans,
    IReadOnlyList<OilFieldPlan> UnusedPlans);
```

In `Planner.cs`, change the `missingPumpjacks` throw to exclude heat drops, and build the summary. Replace:

```csharp
var missingPumpjacks = initialPumpjackCount - context.CenterToTerminals.Count;
if (missingPumpjacks > 0)
{
    throw new FactorioToolsException("The initial number of pumpjacks does not match the final pumpjack count.");
}
```
with:
```csharp
var missingPumpjacks = initialPumpjackCount - context.CenterToTerminals.Count - context.HeatDroppedPumpjacks;
if (missingPumpjacks > 0)
{
    throw new FactorioToolsException("The initial number of pumpjacks does not match the final pumpjack count.");
}
```

Replace the `Validate.HeatPipesCoverAllTargets(context);` call with a count, and pass the new fields into the summary:

```csharp
Validate.CountUnheatedTargets(context, out var unheatedPumpjacks, out var unheatedPipes);
Validate.HeatPipesAreConnected(context);

var planSummary = new OilFieldPlanSummary(
    missingPumpjacks,
    rotatedPumpjacks,
    context.HeatDroppedPumpjacks,
    unheatedPumpjacks,
    unheatedPipes,
    selectedPlans,
    alternatePlans,
    unusedPlans);
```

- [ ] **Step 9: Run the new test and the heat suite**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.HeatOnPartialFieldReportsGapInsteadOfThrowing"`
Expected: PASS (boxed-in field returns a summary with a nonzero unheated gap, no exception).

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest"`
Expected: the two try/catch tests (`EnablingBeaconsNeverBreaksAchievableHeatCoverage`, `HeatOnlyPrefersHeatableLayoutAcrossSmallList`) may now FAIL because partial fields no longer throw - that is expected and fixed in Task 2. `AddsHeatPipesForAquilo`, `AddsHeatPipesAndBeaconsTogetherForAquilo`, `HeatRouterDoesNotStrandReachablePipesBehindEnclosedSeed` should still PASS (those indices are fully heatable, ValidateSolution no longer throws on coverage). Confirm `Score` tests are unchanged.

- [ ] **Step 10: Commit**

```bash
git add src/FactorioTools test/FactorioTools.Test/OilField/PlannerTest.cs
git commit -m "Rank pipe layouts by heat coverage; report partial heat instead of failing

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UA6ptmiP8U8DvtAe9p85Mv"
```

---

## Task 2: Minimal-drop replan loop

Drops the fewest pumpjacks needed (one per pass, minimal-drop) until the selected layout is fully heatable, so heat-on fields output fully heated and connected. Reworks the two tests that relied on the old throw.

**Files:**
- Modify: `src/FactorioTools/OilField/Helpers.cs` (add `RemovePumpjack` next to `AddPumpjack` ~28-41)
- Modify: `src/FactorioTools/OilField/Steps/AddPipes.0.cs` (`Execute` ~11-63: extract `SelectBestSolution`; add `ChooseDropCandidate`, `DropPumpjack`, `IsBefore`)
- Test: `test/FactorioTools.Test/OilField/PlannerTest.cs`

**Interfaces:**
- Consumes: `Solution.UncoveredPipes` / `UncoveredCenters`, `Context.HeatDroppedPumpjacks` (Task 1).
- Produces: `Helpers.RemovePumpjack(SquareGrid grid, Location center)`; behavior that `Planner.Execute` with heat on yields `summary.UnheatedPumpjacks == 0 && summary.UnheatedPipes == 0` and `summary.HeatDroppedPumpjacks >= 0`.

- [ ] **Step 1: Write the failing test**

Add to `PlannerTest.cs`:

```csharp
[Fact]
public async Task DropsPumpjacksToFullyHeatBoxedInField()
{
    // A boxed-in field has no fully-heatable layout for the full pumpjack set. The planner must drop the
    // fewest pumpjacks needed so the rest is fully heated and connected, and report the drop count.
    var options = OilFieldOptions.ForMediumElectricPole;
    options.ValidateSolution = true; // connectivity is validated; coverage is now reported, not thrown
    options.AddHeatPipes = true;
    options.AddBeacons = false;

    var result = Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[BoxedInHeatIndex]));
    var summary = result.Summary;

    Assert.True(summary.HeatDroppedPumpjacks > 0, "expected at least one pumpjack to be dropped on a boxed-in field");
    Assert.Equal(0, summary.UnheatedPumpjacks);
    Assert.Equal(0, summary.UnheatedPipes);
    Assert.NotEmpty(result.Context.Grid.GetEntities().OfType<HeatPipe>());
#if USE_VERIFY
    await Verify(GetGridString(result));
#else
    await Task.Yield();
#endif
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.DropsPumpjacksToFullyHeatBoxedInField"`
Expected: FAIL - `HeatDroppedPumpjacks` is 0 and the gap is nonzero (no drop loop yet).

- [ ] **Step 3: Add `RemovePumpjack` (`Helpers.cs`)**

Directly after `AddPumpjack`:

```csharp
public static void RemovePumpjack(SquareGrid grid, Location center)
{
    for (var x = -1; x <= 1; x++)
    {
        for (var y = -1; y <= 1; y++)
        {
            grid.RemoveEntity(new Location(center.X + x, center.Y + y));
        }
    }
}
```

- [ ] **Step 4: Extract `SelectBestSolution` with the drop loop (`AddPipes.0.cs`)**

Replace the top of `Execute` (the single `GetBestSolution` call + stranded-terminal retry, currently ~25-36) with a call to a new method, keeping the rest of `Execute` (placement at ~38-62) the same:

```csharp
var result = SelectBestSolution(context, eliminateStrandedTerminals);
if (result.Exception is not null)
{
    throw result.Exception;
}

(selectedPlans, alternatePlans, unusedPlans, bestSolution, bestBeacons) = result.Data!;
```

Add the new method. It folds in the existing stranded-terminal retry and wraps the heat-drop loop:

```csharp
private static Result<SolutionInfo> SelectBestSolution(Context context, bool eliminateStrandedTerminals)
{
    // The full pumpjack set's planning maps; the drop loop mutates these in place so each replan
    // (which clones context.CenterToTerminals at the start of GetSolutionGroups) sees the reduced set.
    var centerToTerminals = context.CenterToTerminals;
    var locationToTerminals = context.LocationToTerminals;

    while (true)
    {
        context.CenterToTerminals = centerToTerminals;
        context.LocationToTerminals = locationToTerminals;

        var result = GetBestSolution(context);
        if (result.Exception is NoPathBetweenTerminalsException && !eliminateStrandedTerminals)
        {
            EliminateStrandedTerminals(context);
            result = GetBestSolution(context);
        }

        if (result.Exception is not null)
        {
            return result;
        }

        var best = result.Data!.BestSolution;
        if (!context.Options.AddHeatPipes
            || best.UncoveredPipes.Count + best.UncoveredCenters.Count == 0)
        {
            return result;
        }

        var dropCenter = ChooseDropCandidate(context, best);
        if (!dropCenter.IsValid)
        {
            // Nothing left to drop (e.g. the last pumpjack is boxed by avoid entities). Accept the residual;
            // it is reported on the summary and surfaced as a UI warning.
            return result;
        }

        DropPumpjack(context, centerToTerminals, locationToTerminals, dropCenter);
        context.HeatDroppedPumpjacks++;
    }
}
```

Note: `EliminateStrandedTerminals` only runs on the first pass (when `!eliminateStrandedTerminals`); after a drop the field is strictly smaller, so it does not need to re-run.

- [ ] **Step 5: Add `ChooseDropCandidate` and `IsBefore` (`AddPipes.0.cs`)**

```csharp
// Minimal-drop: remove one pumpjack per pass. Prefer a pumpjack that itself cannot be heated; otherwise the
// pumpjack nearest a stuck (unheated) pipe tile. Deterministic tie-break on (Y, X) for Lua stability.
private static Location ChooseDropCandidate(Context context, Solution best)
{
    if (best.UncoveredCenters.Count > 0)
    {
        var chosenCenter = Location.Invalid;
        foreach (var center in best.UncoveredCenters.EnumerateItems())
        {
            if (IsBefore(center, chosenCenter))
            {
                chosenCenter = center;
            }
        }

        return chosenCenter;
    }

    if (best.UncoveredPipes.Count == 0)
    {
        return Location.Invalid;
    }

    var chosen = Location.Invalid;
    var chosenDistance = int.MaxValue;
    for (var i = 0; i < context.Centers.Count; i++)
    {
        var center = context.Centers[i];
        var distance = int.MaxValue;
        foreach (var pipe in best.UncoveredPipes.EnumerateItems())
        {
            var d = Math.Abs(center.X - pipe.X) + Math.Abs(center.Y - pipe.Y);
            if (d < distance)
            {
                distance = d;
            }
        }

        if (distance < chosenDistance || (distance == chosenDistance && IsBefore(center, chosen)))
        {
            chosenDistance = distance;
            chosen = center;
        }
    }

    return chosen;
}

private static bool IsBefore(Location a, Location b)
{
    if (!b.IsValid)
    {
        return true;
    }

    if (a.Y != b.Y)
    {
        return a.Y < b.Y;
    }

    return a.X < b.X;
}
```

- [ ] **Step 6: Add `DropPumpjack` (`AddPipes.0.cs`)**

```csharp
private static void DropPumpjack(
    Context context,
    ILocationDictionary<List<TerminalLocation>> centerToTerminals,
    ILocationDictionary<List<TerminalLocation>> locationToTerminals,
    Location center)
{
    if (centerToTerminals.TryGetValue(center, out var terminals))
    {
        for (var i = 0; i < terminals.Count; i++)
        {
            var terminal = terminals[i];
            if (locationToTerminals.TryGetValue(terminal.Terminal, out var atLocation))
            {
                atLocation.Remove(terminal);
                if (atLocation.Count == 0)
                {
                    locationToTerminals.Remove(terminal.Terminal);
                }
            }
        }

        centerToTerminals.Remove(center);
    }

    context.Centers.Remove(center);
    context.CenterToOriginalDirection.Remove(center);
    RemovePumpjack(context.Grid, center);
}
```

(`RemovePumpjack` is reachable via the `using static Knapcode.FactorioTools.OilField.Helpers;` already at the top of this file. `Math` needs `using System;` - confirm it is present; add if not.)

- [ ] **Step 7: Run the new test**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.DropsPumpjacksToFullyHeatBoxedInField"`
Expected: FAIL first run on the Verify snapshot (no `*.verified.txt` yet) but the assertions before `Verify` should pass. Accept the snapshot:

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest.DropsPumpjacksToFullyHeatBoxedInField"` then accept the received file via the Verify workflow (`mv` is done by the Verify tooling / `dotnet verify accept`, never hand-write the verified file).
Expected after accept: PASS - `HeatDroppedPumpjacks > 0`, zero residual gap.

- [ ] **Step 8: Rework the two try/catch heat tests**

Replace `EnablingBeaconsNeverBreaksAchievableHeatCoverage` body with the drop-count invariant (no try/catch):

```csharp
[Theory]
[MemberData(nameof(SmallListBlueprintIndexes))]
public void EnablingBeaconsNeverForcesMoreHeatDrops(int index)
{
    // Heat pipes are the hard constraint; beacons are best-effort. Turning beacons on must never force the
    // planner to drop more pumpjacks than heat-only would to keep the field fully heated.
    var blueprintString = SmallListBlueprintStrings[index];

    var heatOnly = OilFieldOptions.ForMediumElectricPole;
    heatOnly.ValidateSolution = true;
    heatOnly.AddHeatPipes = true;
    heatOnly.AddBeacons = false;
    var heatOnlyResult = Planner.Execute(heatOnly, ParseBlueprint.Execute(blueprintString));

    var bothOn = OilFieldOptions.ForMediumElectricPole;
    bothOn.ValidateSolution = true;
    bothOn.AddHeatPipes = true;
    bothOn.AddBeacons = true;
    var bothOnResult = Planner.Execute(bothOn, ParseBlueprint.Execute(blueprintString));

    Assert.True(
        bothOnResult.Summary.HeatDroppedPumpjacks <= heatOnlyResult.Summary.HeatDroppedPumpjacks,
        $"beacons on dropped {bothOnResult.Summary.HeatDroppedPumpjacks} pumpjacks vs heat-only {heatOnlyResult.Summary.HeatDroppedPumpjacks}");
    Assert.Equal(0, bothOnResult.Summary.UnheatedPumpjacks);
    Assert.Equal(0, bothOnResult.Summary.UnheatedPipes);
}
```

Replace `HeatOnlyPrefersHeatableLayoutAcrossSmallList` body (no try/catch):

```csharp
[Fact]
public void HeatOnlyFullyHeatsEveryFieldAndRarelyDrops()
{
    // With per-layout heat ranking plus minimal-drop, every small-list field comes out fully heated. Most need
    // zero drops (the layout itself is heatable); only the dense boxed-in fields drop any pumpjacks.
    var zeroDrop = 0;
    for (var index = 0; index < SmallListBlueprintStrings.Count; index++)
    {
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        options.AddHeatPipes = true;
        options.AddBeacons = false;

        var summary = Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[index])).Summary;

        Assert.Equal(0, summary.UnheatedPumpjacks);
        Assert.Equal(0, summary.UnheatedPipes);
        if (summary.HeatDroppedPumpjacks == 0)
        {
            zeroDrop++;
        }
    }

    Assert.True(zeroDrop >= 35, $"expected at least 35 of {SmallListBlueprintStrings.Count} fields to need zero drops, got {zeroDrop}");
}
```

Update `HeatRouterDoesNotStrandReachablePipesBehindEnclosedSeed` to also assert zero drops (index 6 is fully heatable):

```csharp
    var result = Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[6]));

    Assert.Equal(0, result.Summary.HeatDroppedPumpjacks);
    Assert.NotNull(result.Context.HeatPipes);
    Assert.True(result.Context.HeatPipes!.Count > 1, "the heat network should be more than the seed tile");
```

- [ ] **Step 9: Run the full planner suite + Lua settings**

Run: `dotnet test --filter "FullyQualifiedName~PlannerTest"`
Expected: PASS. If `AddsHeatPipesForAquilo` / `AddsHeatPipesAndBeaconsTogetherForAquilo` snapshots shifted, confirm the diff is only expected heat/beacon changes and accept via Verify.

Run: `dotnet test /p:UseLuaSettings=true --filter "FullyQualifiedName~PlannerTest"`
Expected: PASS (or the documented Lua-subset). Confirm no nondeterminism in drop selection.

Run: `dotnet test --filter "FullyQualifiedName~Score"`
Expected: PASS, `Score.HasExpectedScore.verified.txt` unchanged (non-heat plans untouched).

- [ ] **Step 10: Commit**

```bash
git add src/FactorioTools test/FactorioTools.Test
git commit -m "Drop fewest pumpjacks needed to fully heat boxed-in Aquilo fields

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UA6ptmiP8U8DvtAe9p85Mv"
```

---

## Task 3: Surface the heat warning in the UI and remove the temporary guard

Regenerates the TS client for the new summary fields, adds the drop warning to the plan view, and deletes the beacon/heat mutual-exclusion guard now that the planner supports coexistence.

**Files:**
- Regenerate: `src/WebApp/swagger.json`, `src/vue/src/lib/FactorioToolsApi.ts`
- Modify: `src/vue/src/components/OilFieldPlanView.vue` (warning block near ~13-18)
- Modify: `src/vue/src/stores/OilFieldStore.ts` (delete guard ~111-160, ~169)
- Modify: `src/vue/src/components/BeaconForm.vue` (delete block ~8-16, import/usage ~128, ~143)

**Interfaces:**
- Consumes: `OilFieldPlanSummary` now carrying `heatDroppedPumpjacks`, `unheatedPumpjacks`, `unheatedPipes` (camelCase in JSON / TS).

- [ ] **Step 1: Regenerate `swagger.json` and the TS client**

Building the WebApp runs the `PostBuild` target that rewrites `src/WebApp/swagger.json`, then `swagger-gen` rebuilds the TS interface.

Run:
```bash
dotnet build src/WebApp/WebApp.csproj
cd src/vue && npm install && npm run swagger-gen
```
(If no local .NET 8 SDK: `./docker-build.sh build src/WebApp/WebApp.csproj` then `npm run swagger-gen`.)

Expected: `git diff src/WebApp/swagger.json src/vue/src/lib/FactorioToolsApi.ts` shows `heatDroppedPumpjacks`, `unheatedPumpjacks`, `unheatedPipes` added to `OilFieldPlanSummary`, and the stale "beacons compete with heat pipes ... best with beacons off" comments on the `AddHeatPipes` / `AddBeacons` request props gone or updated (they come from the C# doc-comments; if a C# doc-comment still asserts mutual exclusion, fix it in the core options file and regenerate).

- [ ] **Step 2: Add the heat warning to `OilFieldPlanView.vue`**

After the existing `rotatedPumpjacks` warning block (`~13-18`), add:

```html
    <div
      v-if="plan.data.summary.heatDroppedPumpjacks > 0"
      class="row g-2"
      role="alert"
    >
      <div class="col-12 alert alert-warning" role="alert">
        <b>Some pumpjacks couldn't be heated.</b>
        {{ plan.data.summary.heatDroppedPumpjacks }} pumpjack(s) were dropped so the rest of the field
        stays fully heated and connected on Aquilo.
      </div>
    </div>
    <div
      v-if="plan.data.summary.unheatedPumpjacks + plan.data.summary.unheatedPipes > 0"
      class="row g-2"
      role="alert"
    >
      <div class="col-12 alert alert-warning" role="alert">
        <b>Incomplete heat coverage.</b> Some placed pumpjacks or pipes have no adjacent heat pipe and will
        freeze on Aquilo - the field is too tightly packed to fully heat even after dropping pumpjacks.
      </div>
    </div>
```

- [ ] **Step 3: Remove the guard from `OilFieldStore.ts`**

Delete the `beaconHeatPipeConflictWarning` export, the `mutualExclusionInstalled` flag, the entire `installBeaconHeatPipeMutualExclusion` function (~111-160), and its call inside `getStore` (~169). Remove the now-unused `watch` / `ref` imports only if nothing else in the file uses them (check first - leave imports that other code still needs).

- [ ] **Step 4: Remove the conflict warning from `BeaconForm.vue`**

Delete the `v-if="beaconHeatPipeConflictWarning"` `<div>` block (~8-16). In the `<script>`, remove `beaconHeatPipeConflictWarning` from the import (`~128`) and from the `data()` `Object.assign({ beaconHeatPipeConflictWarning }, ...)` (~143) so it becomes `Object.assign(pick(...))` (or just `return pick(...)`).

- [ ] **Step 5: Type-check, lint, build**

Run:
```bash
cd src/vue && npm run build
```
Expected: `vue-tsc` passes (no reference to the deleted `beaconHeatPipeConflictWarning`, summary fields typed), vite build succeeds.

Run: `npx eslint .` (or the project's lint script)
Expected: clean.

- [ ] **Step 6: Manual smoke check (optional but recommended)**

Run `npm run build-wasm` then `npm run dev`, enable heat pipes + beacons together (the checkbox no longer reverts), plan a boxed-in field, and confirm the "some pumpjacks couldn't be heated" warning renders with a nonzero count.

- [ ] **Step 7: Commit**

```bash
git add src/WebApp/swagger.json src/vue/src
git commit -m "Show heat-drop warning and remove beacon/heat mutual-exclusion guard

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01UA6ptmiP8U8DvtAe9p85Mv"
```

---

## Self-Review

**Spec coverage:**
- Misleading-error fix / rank-by-coverage -> Task 1 Steps 3-5.
- Non-throwing validation -> Task 1 Step 7.
- Summary fields + `missingPumpjacks` adjustment -> Task 1 Step 8.
- Minimal-drop loop + deterministic candidate + `DropPumpjack`/`RemovePumpjack` -> Task 2 Steps 3-6.
- Beacon planning gate kept on `heatFeasible` -> Task 1 Step 4 (unchanged gate).
- Test reworks (both try/catch tests, index-6 test, new boxed-in test + snapshot) -> Task 1 Step 1, Task 2 Steps 1, 8.
- UI warning + guard removal + TS regen -> Task 3.
- Lua determinism -> `IsBefore` tie-break (Task 2 Step 5); Lua test run (Task 2 Step 9).
- No-change-to-non-heat-plans -> Score check (Task 2 Step 9).

**Placeholder scan:** `BoxedInHeatIndex` is the one value resolved empirically (Task 1 Step 2) - flagged with a concrete starting value (55) and a fallback procedure, not a bare TODO.

**Type consistency:** `Route(... out ILocationSet uncoveredPipes, out ILocationSet uncoveredCenters)`, `Solution.UncoveredPipes`/`UncoveredCenters`, `Context.HeatDroppedPumpjacks`, `Validate.CountUnheatedTargets(... out int unheatedPumpjacks, out int unheatedPipes)`, and `OilFieldPlanSummary(... int HeatDroppedPumpjacks, int UnheatedPumpjacks, int UnheatedPipes ...)` are used consistently across tasks. TS camelCase counterparts (`heatDroppedPumpjacks`, `unheatedPumpjacks`, `unheatedPipes`) match in Task 3.
