# Planner progress bar (opt-in, determinate)

Date: 2026-06-24

## Goal

Replace the indeterminate (and, in practice, frozen) spinner next to "Plan oil
field" with a determinate progress bar that shows roughly how far along the plan
is - "strategy N of M". The bar is an opt-in advanced option, off by default.

## Background and the core constraint

The Vue app plans in-browser by calling the .NET WASM export
`Interop.Plan(requestJson)` (`src/vue/src/lib/wasmPlanner.ts`). That call is
synchronous and runs on the browser's main thread on purpose: the
single-threaded .NET WASM runtime deadlocks during `create()` when booted in a
Web Worker, so the worker approach was reverted (PR #16).

Because the call blocks the main thread, the browser cannot repaint while it
runs - even the current CSS spinner freezes mid-plan. Therefore a live-updating
progress bar cannot simply be bolted onto the existing single call. The work
must be split into chunks, with control handed back to the browser between
chunks so it can paint.

The planner has a natural chunk boundary: it tries a list of pipe strategies
(up to five) and, for each resulting pipe solution, a list of beacon strategies
(up to three), then selects the best plan by a fixed ranking. See
`src/FactorioTools/OilField/Steps/AddPipes.0.cs:434` and
`src/FactorioTools/OilField/Steps/PlanBeacons.0.cs:30`.

## Approach: JavaScript-driven per-strategy loop

Drive the strategy loop from JavaScript instead of making the C# planner
internally resumable. For an opt-in progress run, JavaScript calls the existing
`Plan` export once per selected pipe strategy, updating the bar and yielding the
main thread between calls.

This was chosen over a resumable in-WASM session because it requires essentially
no change to the perf-tuned, Lua-transpiled core planner - the code the project
deliberately avoids perturbing. The cost is coarser granularity and some
redundant work per call (see Trade-offs).

### Step unit and progress count

- One step = one selected pipe strategy. Each per-strategy call still runs all
  selected beacon strategies internally for that strategy's pipe solution(s).
- Total steps = number of selected pipe strategies (the strategies the user
  checked, 1-5).
- The bar advances in discrete jumps and unevenly, because strategies differ in
  cost (e.g. Flute is slower than FBE). This is acceptable for coarse
  "how far along" feedback.

### Best-result selection across calls

Each per-strategy response carries `Summary.SelectedPlans[0]`, an `OilFieldPlan`
with the exact ranking fields the planner uses
(`src/FactorioTools/OilField/OilFieldPlan.cs`):

1. Higher `BeaconEffectCount` is better.
2. For equal effects, lower `BeaconCount` is better.
3. For equal effects and beacons, lower `PipeCount` is better.

JavaScript iterates the selected pipe strategies in their existing order, keeps
the response whose `SelectedPlans[0]` ranks best, and on an exact three-field tie
keeps the earlier strategy (strictly-better replaces; equal does not). The kept
response (including its `Blueprint` and `Summary`) is returned as the result,
exactly as `getPlan()` returns today.

## Detailed design

### C#: no core changes

`PlanOrchestrator`, `Planner.Execute`, and the `Interop.Plan` export are
unchanged. The request already exposes `pipeStrategies` as a list; sending a
single-element list runs exactly one pipe strategy. No Lua/build-flag surface is
touched.

### `src/vue/src/lib/wasmPlanner.ts`: unchanged

Still exposes `plan(requestJson)` / `normalize(requestJson)` as a single call
each. The progress loop calls `plan` repeatedly.

### `src/vue/src/lib/OilFieldPlanner.ts`: new `getPlanWithProgress`

Add:

```ts
// `current` = number of strategies COMPLETED so far (starts at 0). The strategy
// currently running is therefore number `current + 1` of `total`.
export type PlanProgress = { current: number; total: number; label: string }

export async function getPlanWithProgress(
  onProgress: (p: PlanProgress) => void,
): Promise<ApiResult<OilFieldPlanResponse> | ApiError>
```

Behavior:

1. Build the base request from the store exactly as `getPlan()` does (reuse
   `requestPropertyGetters`).
2. Read the selected pipe strategies from the base request's `pipeStrategies`.
   - If empty, fall through to a single normal `getPlan()` call (the planner
     itself handles an empty list / validation today; do not diverge).
   - If exactly one, this is effectively one step - still go through the loop so
     the bar shows "1 of 1".
3. For each strategy `i` (0-based) in order:
   - `onProgress({ current: i, total, label })` where `current` is the number
     completed so far (= `i`) and `label` names the strategy about to run (e.g.
     "FBE", "CC-DT", "CC-FLUTE"; reuse the `OilFieldPlan.ToString` naming). The UI
     shows the running strategy as `current + 1` of `total`.
   - `await` a macrotask yield: `await new Promise((r) => setTimeout(r, 0))` so
     the browser paints the updated bar before the next blocking call. (Mirrors
     the existing 10ms paint yield in `OilField.vue:389`.)
   - Build a request clone with `pipeStrategies: [strategy]`, call
     `wasmPlanner.plan(JSON.stringify(clone))`, parse via the same error-envelope
     handling `runWasm` already implements (factor the parse/envelope logic so it
     is shared, not duplicated).
   - If the parsed result is an error, abort the loop and return that error
     immediately (do not silently continue).
   - Otherwise compare to the current best and keep the better one.
4. After the loop, `onProgress({ current: total, total, label: "Finalizing" })`
   and return the best result.

The existing `getPlan()` stays as-is for the default (progress-off) path.

### `src/vue/src/stores/OilFieldStore.ts`: `showProgress` setting

- Add `showProgress: false` to `defaults`.
- Add a `storeToQuery` entry (the `StoreToQuery` mapped type requires every state
  key except `usingQueryString`/`useStagingApi`), e.g. `showProgress: "progress"`.
- It persists and round-trips through the query string like the other booleans.

### `src/vue/src/components/PlannerForm.vue`: advanced checkbox

Add a checkbox bound to `showProgress`, alongside the existing
`optimizePipes` / `validateSolution` toggles, within the advanced options area
(gated by the existing `useAdvancedOptions`). Label: "Show planning progress
(slower)". Help text: notes it plans one strategy at a time so the bar can
update, which is a bit slower than a single combined run.

### `src/vue/src/views/OilField.vue`: progress state and bar

- Add reactive `planProgress: PlanProgress | null = null`.
- In `submit()`, branch:
  - If `store.showProgress` is true, call `getPlanWithProgress((p) => { this.planProgress = p })`.
  - Else call `getPlan()` as today.
- Wrap in the existing `invokeApi` flow; in its `finally`, also reset
  `this.planProgress = null` (in addition to `submitting = false`).
- Rendering under the submit button:
  - When `submitting` and `planProgress` is set, render a Bootstrap progress bar:
    `<div class="progress">` + `<div class="progress-bar" :style="{ width: pct + '%' }" :aria-valuenow="pct" aria-valuemin="0" aria-valuemax="100">` showing e.g. "FBE (1 of 4)" (running strategy = `current + 1` of `total`). Bar width reflects completed work: `pct = round(current / total * 100)`.
  - When `submitting` without `planProgress` (default path, e.g. normalize), keep
    today's spinner.
- The button's existing spinner stays for the default path; the progress bar is
  additive and only appears in progress mode.

## Edge cases

- **One pipe strategy selected:** loop runs once; bar shows "1 / 1". No
  regression, minimal benefit, no harm.
- **Single pumpjack field** (`CenterToTerminals.Count == 1`): each call resolves
  the single trivial solution; bar jumps 0 -> 100 in one step per strategy.
- **Empty pipe-strategy list:** defer to a single `getPlan()` call so behavior and
  validation match today exactly.
- **Error mid-loop:** return the first error encountered and stop; do not show a
  partial/best-of-the-successes result, to avoid surfacing a plan the user did not
  fully get.
- **Tie on all three ranking fields:** keep the earliest strategy's blueprint
  (see Trade-offs for the faithfulness note).

## Trade-offs (accepted, documented)

- **Slower in progress mode.** Each per-strategy call re-runs context
  initialization (blueprint parse, terminal setup) and loses the monolithic
  planner's cross-strategy dedup (`completedStrategies`,
  `connectedCentersToSolutions`). Only opt-in runs pay this; the default path is
  unchanged.
- **Rare tie-break divergence.** The monolithic planner collects equivalent plans
  into `SelectedPlans` / `AlternatePlans` and emits one. The JS loop picks the
  earliest strategy among exact-metric ties, which may emit a different but
  equally-optimal blueprint (identical `BeaconEffectCount` / `BeaconCount` /
  `PipeCount`). Result quality by the project's own ranking is unchanged.

## Out of scope (explicit non-goals)

- **Cancel button.** A previous worker-based cancel was reverted with the worker.
  Per-step cancel is a natural future extension (stop calling between steps) but
  is not part of this change.
- **Smooth/continuous bar.** Would require instrumenting inside each strategy's
  A*/Steiner loops in the perf-tuned, Lua-transpiled core. Not pursued.
- **Web Worker / multithreaded WASM.** Requires the multithreaded build,
  SharedArrayBuffer, and COOP/COEP cross-origin-isolation headers; deliberately
  avoided.

## Testing

- The Vue app has no JS test harness today; verify manually:
  - `npm run build-wasm` then `npm run build` + `npm run preview` (the WASM bundle
    does not run under `npm run dev`).
  - With `showProgress` off: behavior and output identical to today (spot-check a
    known blueprint's resulting plan summary).
  - With `showProgress` on: the bar advances per strategy and the final blueprint
    matches the progress-off result for the same inputs (modulo the documented
    tie-break edge case).
- No `.NET` snapshot changes are expected, since core planning is untouched. If
  any `*.verified.txt` snapshot moves, that is a signal something diverged and
  must be investigated rather than accepted.
