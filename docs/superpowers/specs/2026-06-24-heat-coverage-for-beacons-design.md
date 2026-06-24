# Heat coverage for beacons on Aquilo

Date: 2026-06-24
Branch: `feat/heat-beacons`

## Problem

On Aquilo (Factorio 2.0 / Space Age) unheated entities freeze. The planner already
routes a connected heat pipe network adjacent to every pumpjack and every pipe, but
beacons are placed *around* the already-routed heat network and are never guaranteed an
adjacent heat pipe. A beacon draws 400 kW of heat and is NOT freeze-immune, so an
unheated beacon freezes and provides no effects. Today, when heat pipes and beacons are
both enabled, some beacons in the output blueprint are functionally dead.

The root cause is ordering: heat is routed per pipe layout *before* beacons exist
(`AddPipes.0.cs` `GetSolution`), and beacons deliberately route around the heat network
(heat is the hard constraint, beacons take the leftover space). At heat-routing time the
beacon positions are not known.

A secondary problem surfaced while scoping this: the WASM planner boots and runs on the
browser main thread (`src/vue/src/lib/wasmPlanner.ts` calls `Interop.Plan` synchronously),
so a long plan freezes the entire page - no spinner, no cancel. Heating beacons adds work,
so this is the moment to move planning off the main thread.

## Goals

1. Heat as many placed beacons as possible by extending the heat network to reach them.
2. Drop beacons that still cannot be heated, so the output blueprint contains only working
   beacons (no dead weight inflating beacon count / power).
3. Move browser planning off the main thread so the UI stays responsive (spinner + cancel)
   during long plans.

## Non-goals

- Heat-aware beacon placement / co-planning (rewriting the FBE / Snug strategies to prefer
  heatable positions). Deferred.
- Honest-scoring across all beacon strategies. We deliberately take the fast path (see
  "Selection" below): selection ranks on pre-drop effect counts, and the extension + drop
  runs once on the winning plan.
- True multi-core parallelism (N WASM workers across strategy combos, or `WasmEnableThreads`
  with COOP/COEP). A single Web Worker is in scope; parallelism is a separate follow-up.

## Design

### Part 1 - Heat extension for beacons (core library)

`GetSolution` is unchanged. Pass-1 heat (pipes + pumpjacks) and beacon planning stay exactly
as today; plan selection ranks on pre-drop beacon effect counts as it does now. No new fields
on `Solution` or `BeaconSolution`.

All new core work happens once, in `AddPipes.Execute`, right after the winning plan is
materialized (`src/FactorioTools/OilField/Steps/AddPipes.0.cs`, currently lines 36-55:
pipes -> base heat -> beacons). The new step runs only when `context.Options.AddHeatPipes &&
context.Options.AddBeacons` and after `AddBeaconsToGrid` has placed the winning beacons:

1. **`AddHeatPipes.ExtendToBeacons(context, beaconCenters)`** (new public method):
   - Computes each beacon's footprint from `context.Options.BeaconWidth` /
     `BeaconHeight` (so custom beacon sizes work), rather than the hardcoded 3x3
     `PumpjackRingOffsets`. A beacon is "heated" when a heat tile is orthogonally adjacent
     to any tile of its footprint.
   - Builds candidate heat tiles = empty tiles orthogonally adjacent to any beacon
     footprint, mapping each candidate to the beacons it would heat (same `AddCoverage` /
     `CountCovered` machinery as `RouteCore`).
   - **Seeds the grow from the already-placed `context.HeatPipes` network** (not a fresh
     seed): the existing network is the starting `chosen` set, and the grow only adds tiles
     and bridges to reach beacon candidates, reusing `Grow` / `BridgeToTile` / `Gain` /
     `IsAdjacentToNetwork`. Because base heat is already on the grid as `HeatPipe` entities,
     `BridgeToTile` seeds its BFS from those `Location`s and walks outward through genuinely
     empty tiles - no grid-state conflict (the beacons and pipes occupy the grid, so the
     search routes around them).
   - Returns the set of extra heat tiles chosen and the set of beacons that ended up heated.
   - Implementation note: extract the existing `Grow` while-loop into a helper that accepts a
     pre-seeded `chosen` set so both `RouteCore` (fresh seed) and `ExtendToBeacons`
     (pre-seeded) share it; or add a seeded entry point. Keep the change inside
     `AddHeatPipes.cs`.

2. Place the extra heat tiles as `HeatPipe` entities and union them into `context.HeatPipes`.

3. **Drop** every beacon not in the heated set: remove its `BeaconCenter` from the grid.

4. Update the selected plan's reported `BeaconCount` and `BeaconEffectCount` to the post-drop
   reality so the summary matches the blueprint. Effect recompute: sum the surviving beacons'
   per-beacon effect contributions. If the per-beacon effect count is not already reachable at
   this point, carry it from beacon planning to the materialization step (the FBE / Snug
   planners already compute `EffectsGivenCount` per beacon).

### Part 2 - Validation (core library)

Extend `Validate` so that, when heat and beacons are both on:
- every *kept* beacon footprint has an orthogonally adjacent heat pipe, and
- the extension heat tiles overlap nothing (reuse the existing no-overlap checks).

### Part 3 - Web Worker for browser planning (`src/vue`)

Move the WASM runtime off the main thread so planning does not freeze the page.

1. **`src/vue/src/lib/planner.worker.ts`** (new): hosts the WASM boot logic currently in
   `wasmPlanner.ts` (the `dotnet.js` dynamic import and `getAssemblyExports`). On a posted
   message `{ id, op: "plan" | "normalize", requestJson }` it runs the corresponding
   synchronous `Interop` export and posts back `{ id, responseJson }` or `{ id, error }`.
   Instantiated with Vite's worker URL form
   (`new Worker(new URL("./planner.worker.ts", import.meta.url), { type: "module" })`).
   Single, non-threaded worker - no SharedArrayBuffer and no COOP/COEP headers required.

2. **`src/vue/src/lib/wasmPlanner.ts`** becomes a thin main-thread client that keeps the
   same `plan(json): Promise<string>` / `normalize(json): Promise<string>` interface, so
   `OilFieldPlanner.ts` (`runWasm`, `getPlan`, `normalize`) is unchanged. It owns the worker
   instance and a request-id -> promise map, lazily creating the worker on first use.

3. **Cancellation.** A synchronous WASM call cannot be interrupted from inside (single-threaded
   WASM, no cooperative cancel in the core). Cancel = `worker.terminate()` plus rejecting the
   in-flight promise; the next plan lazily boots a fresh worker. Re-boot cost is acceptable for
   an explicit cancel (browser-cached assets make it faster than the first load).

4. **UI (`src/vue/src/views/OilField.vue`).** The existing `submitting` spinner on the Plan
   button (lines 107-113) now actually animates because the main thread is free. Add a Cancel
   button shown while `submitting` that calls a new cancel path (terminate worker, reset
   `submitting`, clear in-flight state). Keep a brief informational note shown when
   `addHeatPipes && addBeacons` are both selected: planning runs entirely in your browser and
   large fields can take a while. Plain hyphens, no em/en dashes.

## Files touched

- `src/FactorioTools/OilField/Steps/AddHeatPipes.cs` - `ExtendToBeacons`, seeded grow helper.
- `src/FactorioTools/OilField/Steps/AddPipes.0.cs` - extension + drop + stat update in `Execute`.
- `src/FactorioTools/OilField/Steps/Validate.cs` - kept-beacon heat-adjacency invariant.
- `src/vue/src/lib/planner.worker.ts` - new worker.
- `src/vue/src/lib/wasmPlanner.ts` - thin worker client.
- `src/vue/src/views/OilField.vue` - cancel button, live spinner, informational note.

## Testing

- Core: a small-list test asserting every kept beacon is heat-adjacent when heat + beacons are
  both on. A guarantee test analogous to the existing
  `EnablingBeaconsNeverBreaksAchievableHeatCoverage`.
- Expect `Score.HasExpectedScore.verified.txt` and related Verify snapshots to shift (beacon
  counts / effects change on Aquilo combos). Update via the Verify accept workflow, not by hand.
- Build and test under default **and** `UseLuaSettings=true` (CI builds many flag combos; core
  data-structure-adjacent changes must be green under both).
- Front-end: manual verification that the Plan button spinner animates and Cancel aborts a long
  plan without freezing the page (`npm run dev`, then `npm run build-wasm` to refresh the bundle
  after core changes).

## Lua compatibility

`AddHeatPipes` changes must stay Lua-safe: no LINQ, `yield return`, named tuples, try/catch, or
struct dictionary keys in the routing paths; reuse the existing `ILocationSet` /
`ILocationDictionary` containers and deterministic iteration. The Web Worker work is front-end
only and does not affect the transpiled core.

## Risks / open items

- A beacon fully boxed in by pipes, pumpjacks, or other beacons with no empty ring tile is
  un-heatable and will be dropped - same RC2 limitation as the pipe/pumpjack router; acceptable.
- Reported plan stats must be updated after the drop or the summary will disagree with the
  blueprint; called out explicitly in Part 1 step 4.
- The Vite worker bundle must receive the same `__BASE_PATH__` define used to resolve
  `framework/dotnet.js`; verify defines apply to the worker build.
