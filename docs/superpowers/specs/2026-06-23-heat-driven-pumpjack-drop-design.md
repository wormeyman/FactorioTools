# Heat-driven pumpjack drop + beacon/heat coexistence guard removal

Date: 2026-06-23
Branch: `feat/heat-driven-pumpjack-drop`

## Problem

Two related issues, both surfaced by a user running the planner with heat pipes on:

1. **Misleading error.** On a "boxed-in" field with heat pipes enabled, the planner
   throws `"At least one pipe strategy must be used."` This message is wrong about
   the cause. The real cause: when heat is on, `AddPipes.GetAllPlans`
   (`src/FactorioTools/OilField/Steps/AddPipes.0.cs:184-196`) drops every pipe
   layout that cannot be fully heated (`requireHeatFeasible && !solution.HeatFeasible`).
   When no layout is fully heatable (about 26 of 61 small-list blueprints - pipes
   packed so tight a tile has all four orthogonal neighbors occupied), every plan is
   dropped, `sortedPlans` ends up empty, and the generic "at least one pipe strategy"
   error fires. `ValidateSolution` is `false` in production (WASM), so this is the
   `AddPipes` filter, not `Validate`.

2. **Temporary UI guard still in place.** A temporary Vue guard makes beacons and
   heat pipes mutually exclusive (`src/vue/src/stores/OilFieldStore.ts:111-160`,
   warning in `src/vue/src/components/BeaconForm.vue:9-15`). The planner now supports
   them coexisting (commit `14eed8a`, locked by the `PlanHeatPipesAndBeaconsTogetherForAquilo`
   snapshot and the `EnablingBeaconsNeverBreaksAchievableHeatCoverage` guarantee
   test), so the guard is obsolete. Its own comment says "Remove this once the
   planner can coexist."

## Goal

When heat pipes are enabled, the output is **always a fully heated, fully connected
field**. If the full pumpjack set cannot be fully heated on a given field, the
planner drops the **fewest pumpjacks needed** (minimal-drop, greedy) to make the
remaining set fully heatable, keeps as many pumpjacks as possible, and reports how
many it dropped so the UI can warn. Beacons and heat can be enabled together.

**No change to non-heat plans.** Every behavior below is gated on
`context.Options.AddHeatPipes`.

This was chosen over two alternatives:
- *Place-everything-and-warn* (leave cold pumpjacks in the output): rejected - the
  user wants the output fully heated and connected, not partially.
- *Heat-aware pipe routing* (keep all pumpjacks, route pipes to leave heat room):
  the ideal outcome but the largest algorithmic change, explicitly deferred by the
  prior spec, and may still fail the densest fields. Deferred again.

## Relevant existing behavior (confirmed)

- `AddHeatPipes.Route(context, pipeTiles, out coversAllTargets)` already routes the
  heat network for a pipe layout without placing entities and reports whether the
  layout is fully heatable. `RouteCore` computes coverage from `uncoveredPipes.Count`
  and `uncoveredCenters.Count`.
- `GetSolution` (`AddPipes.0.cs:401-455`) routes heat per layout when heat is on and
  records `HeatPipes` + `HeatFeasible` on the `Solution`. Beacon planning is gated on
  `AddBeacons && heatFeasible`.
- `GetAllPlans` drops `!HeatFeasible` layouts; `GetBestSolution` throws the
  "at least one pipe strategy" error when no plan survives.
- `Validate.HeatPipesCoverAllTargets` throws on any unheated pumpjack/pipe;
  `Validate.HeatPipesAreConnected` throws if the placed network is not one connected
  component. Both gated on `ValidateSolution && AddHeatPipes`. `ValidateSolution`
  defaults `false`; only `ExecuteSample()` and tests set it `true`.
- `Planner.Execute` computes `missingPumpjacks = initialPumpjackCount -
  context.CenterToTerminals.Count` and throws if `> 0`. This is the electric-pole
  drop path; it must not fire for heat-driven drops.
- `OilFieldPlanSummary` carries `MissingPumpjacks`, `RotatedPumpjacks`, and the plan
  lists. `OilFieldPlanView.vue:13-18` already renders a Bootstrap `alert-warning`
  for `rotatedPumpjacks > 0` - the model for the new heat warning.
- Pumpjack centers and terminals are on the grid / in `context.Centers`,
  `context.CenterToTerminals`, `context.LocationToTerminals` before `AddPipes` runs.
  `EliminateStrandedTerminals` and the electric-pole retry already mutate the
  pumpjack set and replan, so re-entrant planning has precedent.

## Design

### 1. Per-layout unheated count, ranked instead of dropped

- Extend `AddHeatPipes.Route` / `RouteCore` to report the residual unheated counts
  (unheated pipes, unheated pumpjack centers), not just the `coversAllTargets` bool.
  `coversAllTargets` becomes `unheatedPipes == 0 && unheatedCenters == 0`.
- Record the unheated pumpjack count and unheated pipe count on the `Solution`
  (the same two figures surfaced later as `UnheatedPumpjacks` / `UnheatedPipes`).
- In `GetAllPlans`, **remove** the `requireHeatFeasible` drop. Instead carry the
  unheated count onto the `PlanInfo` / `OilFieldPlan` so selection can see it.
- In the `GetBestSolution` sort comparator, when heat is on, add the unheated count
  as the **top sort key (ascending)** - ahead of beacon effect count. Fully heatable
  layouts (0 unheated) outrank partial ones; among equally heated layouts the
  existing criteria (beacon effects, beacon count, pipe count, group size, tie
  breaks) apply unchanged. When heat is off, the comparator is unchanged.

This alone removes the misleading error: the plan list is never empty just because
nothing is fully heatable - the most-heatable layout is selected instead.

### 2. Minimal-drop replan loop in `AddPipes.Execute`

Wrap the existing `GetBestSolution` call in a loop:

```
loop:
    result = GetBestSolution(context)        // ranks by unheated count when heat on
    best = result.BestSolution
    if heat off OR best is fully heatable (unheated == 0) OR no droppable pumpjack:
        break
    center = ChooseDropCandidate(context, best)
    DropPumpjack(context, center)            // mutate grid + Centers + terminal maps
    droppedForHeat++
place pipes -> heat -> beacons for `best` (unchanged placement code)
```

- **Termination:** each pass removes exactly one pumpjack, so the loop runs at most
  N times (N = pumpjack count). Guaranteed to terminate.
- **`ChooseDropCandidate` (minimal-drop, deterministic):**
  1. If any pumpjack **center** is unheated in `best`, drop one of those (it can
     never be heated in this layout and removing it directly removes an unheated
     target).
  2. Otherwise (only pipes are unheated) drop the pumpjack **nearest** an unheated
     pipe tile (Manhattan distance from the pumpjack center).
  3. Ties broken by lowest `(Y, X)` so the choice is deterministic under Lua's
     modified `pairs()` / `math.random()`.
- **`DropPumpjack`:** remove the `PumpjackCenter` entity from the grid; remove the
  center from `context.Centers`; remove its terminals from `context.CenterToTerminals`
  and `context.LocationToTerminals`. The next `GetBestSolution` replans pipes + heat
  for the reduced set. Record the dropped center (count, and optionally the set, for
  the summary).

This is greedy: it keeps many but not provably the maximum number of pumpjacks.

### 3. Beacon planning gate

Keep the existing `if (AddBeacons && heatFeasible)` gate in `GetSolution`. Partial
layouts (transient loop iterations, or a rare residual) skip beacon planning; the
final selected layout is normally fully heatable, so beacons are planned on it.
Because unheated count is the top sort key, a feasible layout (with beacons) always
outranks a partial one, so this is consistent.

### 4. Validation

- `Validate.HeatPipesCoverAllTargets` becomes **non-throwing**: it counts residual
  unheated pumpjacks and pipes (for the summary) and never throws. The loop normally
  drives residual to 0; a rare boxed residual (e.g. avoid-entities boxing the last
  pumpjack) is reported, not fatal.
- `Validate.HeatPipesAreConnected` stays **throwing**: whatever heat network is
  placed must be one connected component (the greedy router grows one connected
  network, so this holds; keep it as a guard).

### 5. Summary + UI

- `OilFieldPlanSummary` gains:
  - `HeatDroppedPumpjacks` (int) - pumpjacks removed to make the field heatable.
  - `UnheatedPumpjacks`, `UnheatedPipes` (int) - residual still-cold targets in the
    final output (normally 0).
- `Planner.Execute`: subtract heat-driven drops from the `missingPumpjacks` throw so
  it only fires for the electric-pole category. Report `HeatDroppedPumpjacks` (and
  the residual counts) on the summary.
- `OilFieldPlanView.vue`: add an `alert-warning` (same shape as the existing
  `rotatedPumpjacks` block) shown when `summary.heatDroppedPumpjacks > 0`:
  > **Some pumpjacks couldn't be heated.** N pumpjack(s) were dropped so the rest of
  > the field stays fully heated and connected on Aquilo.
  If `unheatedPumpjacks > 0 || unheatedPipes > 0` (rare residual), extend the message
  to note some placed entities will still freeze.
- The new summary fields reach the Vue client through the regenerated TS API client
  (`npm run swagger-gen` / `npm run build`).

### 6. Remove the temporary guard

- `src/vue/src/stores/OilFieldStore.ts`: delete `beaconHeatPipeConflictWarning`,
  `installBeaconHeatPipeMutualExclusion`, and the call in `getStore`.
- `src/vue/src/components/BeaconForm.vue`: remove the conflict-warning block
  (lines 9-15) and its import / usage of `beaconHeatPipeConflictWarning`.
- Refresh the now-stale "beacons compete with heat pipes ... best with beacons off"
  comments in the generated `FactorioToolsApi.ts` (via regeneration) and any C#
  doc-comments that assert the two cannot coexist.

## Testing

xUnit + Verify. Build and test under default **and** `/p:UseLuaSettings=true`.

- **Rework `EnablingBeaconsNeverBreaksAchievableHeatCoverage`:** drop the
  `try/catch` on `FactorioToolsException`. New coexistence invariant: for every
  small-list field, `bothOn.HeatDroppedPumpjacks <= heatOnly.HeatDroppedPumpjacks`
  (enabling beacons never forces more drops than heat-only).
- **Rework `HeatOnlyPrefersHeatableLayoutAcrossSmallList`:** drop the `try/catch`.
  New invariants: every small-list field's output is fully heated
  (`UnheatedPumpjacks == 0 && UnheatedPipes == 0`); at least 35 of 61 need **zero**
  drops (`HeatDroppedPumpjacks == 0`).
- **`HeatRouterDoesNotStrandReachablePipesBehindEnclosedSeed`** (index 6): unchanged
  intent; assert `HeatDroppedPumpjacks == 0` and heat network `> 1` tile.
- **`AddsHeatPipesForAquilo` / `AddsHeatPipesAndBeaconsTogetherForAquilo`** (index 0):
  heatable, 0 drops; Verify snapshots for these feasible fields stay unchanged.
- **New test + Verify snapshot:** a known boxed-in field (one of the ~26) produces a
  fully heated output (`UnheatedPumpjacks == 0 && UnheatedPipes == 0`) with
  `HeatDroppedPumpjacks > 0`, and `Planner.Execute` does not throw. Identify the index
  during implementation.
- Re-accept any other heat snapshots that legitimately shift via the Verify
  received/verified workflow (not by hand).

## Lua compatibility

All new logic is plain `int` counters and deterministic scans - no `yield return`,
LINQ, named tuples, try/catch, or struct dictionary keys in hot paths. Drop-candidate
selection iterates in a stable order and breaks ties on `(Y, X)` so the result is
deterministic under Factorio's modified `pairs()` / `math.random()`.

## Risks / notes

- **Cost.** The replan loop only runs heat-on and only iterates when a drop is needed
  (~26 small-list fields, a few drops each). Each pass re-runs the multi-strategy
  pipe planning, so worst-case big boxed-in fields get slower. Bounded by drop count;
  optimize later if it matters.
- **Greedy, not optimal.** Minimal-drop keeps many but not provably the maximum
  number of pumpjacks.
- **Scoreboard.** `Score.HasExpectedScore.verified.txt` runs non-heat plans, which
  are unaffected; it should not change. Confirm.

## Out of scope

- Heat-aware pipe routing / repair (keeping all pumpjacks on the densest fields).
- Optimal (vs greedy) pumpjack-subset selection.
