# Heat-router coverage: incremental improvement

Date: 2026-06-23
Branch: `feat/heat-pipes-beacons-coexist`

## Problem

With heat pipes enabled and beacons off, the Aquilo heat router fully heats only
19 of 61 small-list blueprints. The other 42 throw "A pipe at (x, y) ... is not
adjacent to a heat pipe and would freeze on Aquilo." (from
`Validate.HeatPipesCoverAllTargets`).

A diagnostic run across the whole small list (rather than the single masking
blueprint the old `AddsHeatPipesForAquilo` test used) shows the 42 failures split
into two mechanically different root causes:

- **RC1 - the router gets stuck (a bug).** `AddHeatPipes.Grow` seeds the network
  with the highest-coverage empty tile, but that tile can sit in a fully enclosed
  pocket (all four orthogonal neighbors occupied). `BridgeToTile` only ever BFSes
  outward from the chosen network through empty tiles, so an enclosed seed can
  never expand and the whole field is abandoned after a single heat pipe. Index 6
  is the extreme case: 1 heat pipe placed, 44 perfectly reachable pipes left
  uncovered, zero genuinely boxed-in tiles. Four fields fail purely from this
  (boxed-in count 0); several more abandon large reachable regions on top of a
  smaller genuine-box count.

- **RC2 - pipes packed too tight (geometric).** About 38 fields have at least one
  pipe tile whose four orthogonal neighbors are all occupied by other pipes or
  pumpjacks. No heat router can cover these - the pipe layout itself must change.
  Index 55 shows a healthy 171-tile network with 10 such buried pipes. Underground
  pipe planning does not help: it only converts straight runs that have no pipes
  beside them, which by definition excludes a boxed-in tile.

This spec covers an incremental, low-risk pass at RC1 and a selection-side change.
RC2 (heat-aware pipe routing / repair) is intentionally deferred; we re-measure
after these changes and reassess.

## Relevant existing behavior (confirmed)

- `OilFieldOptions` defaults: `UseUndergroundPipes = true`, `OptimizePipes = true`.
  These were active in the diagnostic, so the boxed-in tiles survive the existing
  underground/optimize machinery.
- `PlanUndergroundPipes.Execute` mutates the pipe set in place, removing buried
  middle tiles (`pipes.Remove(...)`), and runs before heat routing inside
  `AddPipes.GetSolution`. `UndergroundPipe : Pipe`, so endpoints remain heat
  targets; removed middles do not.
- The coexistence code already routes heat per candidate pipe layout, but only
  when `AddHeatPipes && AddBeacons` (`GetSolution`), and only drops heat-infeasible
  layouts when both are on (`GetAllPlans`). In heat-only mode the planner picks the
  fewest-pipe layout and routes heat once at the end, so a heatable layout can lose
  to an unheatable one.

## Part 1 - RC1: robust seed selection in `AddHeatPipes.Grow`

Seed with the max-gain candidate that is "expandable" - has at least one empty
orthogonal neighbor - falling back to plain max-gain only if no candidate is
expandable. Boxed candidates can still join the network later through the existing
adjacency fast-path; the expandable constraint applies only to the seed that has
to carry growth.

If re-measurement shows fields still stranding a small grown component (not just a
bad seed), add a bounded "reseed when growth stalls while reachable uncovered
targets with viable candidates remain" step. Do not build this speculatively - let
the measurement decide.

Contained to `AddHeatPipes.cs`; cannot affect any non-heat plan.

## Part 2 - selection-side heat preference in `AddPipes.0.cs`

Compute per-layout heat feasibility whenever `AddHeatPipes` is on, not only when
beacons are also on:

- `GetSolution`: route heat (`AddHeatPipes.Route`) when `context.Options.AddHeatPipes`,
  dropping the `&& context.Options.AddBeacons` condition. Store `HeatPipes` and
  `HeatFeasible` on the solution as today.
- `GetAllPlans`: set `requireHeatFeasible = context.Options.AddHeatPipes`,
  dropping `&& context.Options.AddBeacons`, so heat-infeasible layouts are filtered
  out in heat-only mode too.

The winning layout's `HeatPipes` are already placed in `AddPipes.Execute`, and
`AddHeatPipes.Execute` early-returns when `context.HeatPipes` is set. Among heatable
layouts, selection still prefers fewest pipes. Heat routing runs over the
post-underground surface pipe set, which is already correct.

Activates only when `AddHeatPipes` is on (Aquilo), so heat-off pipe/beacon plans
and the scoreboard are untouched.

## Testing

- Replace the masking single-blueprint heat test with a permanent
  `HeatOnlyCoverage`-style test asserting a coverage threshold across the whole
  small list.
- Drive each change test-first. Re-measure the heated count after Part 1 and again
  after Part 2; report and reassess RC2.
- Remove the throwaway diagnostic tests before finishing.
- Validate under default and `UseLuaSettings=true`. Update Verify snapshots where
  behavior legitimately changes (received -> verified workflow).
- Keep all core-library code Lua-safe: simple loops, no LINQ, yield, named tuples,
  try/catch, or struct dictionary keys in hot paths.

## Out of scope

- RC2: heat-aware pipe routing or post-routing pipe repair for boxed-in tiles.
- Removing the Vue mutual-exclusion guard (tracked separately).
