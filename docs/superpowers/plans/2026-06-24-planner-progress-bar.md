# Planner Progress Bar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in, off-by-default determinate progress bar to the in-browser WASM oil-field planner that shows "strategy N of M" while planning.

**Architecture:** The WASM `Plan()` call blocks the browser main thread, so a live bar requires chunking. JavaScript drives the planner's pipe-strategy loop: it calls the existing `Plan` export once per selected pipe strategy, yields a macrotask between calls so the browser repaints, and keeps the best result using the planner's own ranking (read from `summary.selectedPlans[0]`). The C# planner is untouched. A new opt-in `showProgress` setting gates the behavior; the default path keeps today's single combined call.

**Tech Stack:** Vue 3 (Options API), Pinia, Vite 7, TypeScript, Bootstrap 5, Vitest (new), .NET WASM (unchanged).

## Global Constraints

- The default path (progress off) MUST be unchanged: one combined `wasmPlanner.plan` call via `getPlan()`. (spec: Approach, Trade-offs)
- No changes to C# / the WASM bundle. If any `.NET` `*.verified.txt` snapshot moves, something diverged - investigate, do not accept. (spec: C#: no core changes; Testing)
- One bar step = one selected pipe strategy. Total steps = number of selected pipe strategies. (spec: Step unit and progress count)
- Best-result selection ranking: higher `beaconEffectCount`, then lower `beaconCount`, then lower `pipeCount`; earliest strategy wins exact ties. (spec: Best-result selection across calls)
- Error mid-loop: return the first error immediately; do not continue or return a best-of-successes. (spec: Edge cases)
- Empty pipe-strategy list: defer to a single `getPlan()`-style call. (spec: Edge cases)
- Use hyphens, not em/en dashes, in all files. (project CLAUDE.md)
- Unit tests mock the `wasmPlanner` boundary - no WASM, no DOM. (spec: Unit tests)
- API JSON is camelCase; `PipeStrategy` is a string enum (`"Fbe"`, `"ConnectedCentersDelaunay"`, ...).

---

### Task 1: Vitest harness + `getPlanWithProgress` logic (TDD) + CI

**Files:**
- Modify: `src/vue/package.json` (add `vitest` devDep + `test` scripts)
- Create: `src/vue/vitest.config.ts`
- Modify: `src/vue/src/lib/OilFieldPlanner.ts` (add `PlanProgress`, `getPlanWithProgress`, helpers; DRY `getPlan`)
- Test: `src/vue/src/lib/OilFieldPlanner.test.ts`
- Modify: `.github/workflows/ci.yml` (add a Test step to the `build-vue` job)

**Interfaces:**
- Consumes: existing module-private `runWasm<Data>(requestJson: string, invoke: (json: string) => Promise<string>): Promise<ApiResult<Data> | ApiError>`, `requestPropertyGetters`, `useOilFieldStore`, `getEntries`, and `wasmPlanner.plan` - all already in `OilFieldPlanner.ts`.
- Produces:
  - `export type PlanProgress = { current: number; total: number; label: string }`
  - `export async function getPlanWithProgress(onProgress: (progress: PlanProgress) => void): Promise<ApiResult<OilFieldPlanResponse> | ApiError>`
  - `current` is the count COMPLETED so far (starts at 0); the running strategy is `current + 1` of `total`; a final tick fires with `current === total` and label `"Finalizing"`.

- [ ] **Step 1: Add the Vitest dev dependency and test scripts**

Edit `src/vue/package.json`. Add two scripts to the `"scripts"` block (alongside `dev`, `build`, ...):

```json
    "test": "vitest run",
    "test:watch": "vitest",
```

Then install Vitest (this also updates `package-lock.json`, which CI caches on):

```bash
cd src/vue && npm install -D vitest@^3
```

Expected: `package.json` gains `"vitest": "^3.x"` under `devDependencies`, and `package-lock.json` updates.

- [ ] **Step 2: Add an isolated Vitest config**

Create `src/vue/vitest.config.ts`. Vitest auto-loads `vite.config.ts` otherwise, which shells out to `git` and reads test-data files at load time - we do not want that for unit tests.

```ts
import { defineConfig } from "vitest/config"

// Intentionally standalone (not extending vite.config.ts): the Vite config runs
// git/execSync and reads sample-blueprint files at load time, none of which the
// unit tests need. These tests are pure logic and run in the node environment.
export default defineConfig({
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
})
```

- [ ] **Step 3: Write the failing tests for `getPlanWithProgress`**

Create `src/vue/src/lib/OilFieldPlanner.test.ts`:

```ts
import { beforeEach, describe, expect, it, vi } from "vitest"
import { createPinia, setActivePinia } from "pinia"
import { PipeStrategy } from "./FactorioToolsApi"
import { useOilFieldStore } from "../stores/OilFieldStore"
import { getPlanWithProgress, PlanProgress } from "./OilFieldPlanner"
import * as wasmPlanner from "./wasmPlanner"

vi.mock("./wasmPlanner")

type Metrics = { effects: number; beacons: number; pipes: number }

function planResponse(strategy: PipeStrategy, metrics: Metrics, blueprint: string): string {
  return JSON.stringify({
    request: {},
    blueprint,
    summary: {
      missingPumpjacks: 0,
      rotatedPumpjacks: 0,
      heatDroppedPumpjacks: 0,
      unheatedPumpjacks: 0,
      unheatedPipes: 0,
      selectedPlans: [
        {
          pipeStrategy: strategy,
          optimizePipes: true,
          beaconStrategy: null,
          beaconEffectCount: metrics.effects,
          beaconCount: metrics.beacons,
          pipeCount: metrics.pipes,
          pipeCountWithoutUnderground: metrics.pipes,
        },
      ],
      alternatePlans: [],
      unusedPlans: [],
    },
  })
}

function errorResponse(): string {
  // Matches the error envelope shape runWasm detects: status + errors, no blueprint.
  return JSON.stringify({
    title: "Bad input was provided.",
    status: 400,
    errors: { FactorioToolsException: ["boom"] },
  })
}

// Select exactly the given pipe strategies via the store flags. Unset flags default off.
function selectStrategies(opts: {
  fbeOriginal?: boolean
  fbe?: boolean
  ccDt?: boolean
  ccDtMst?: boolean
  ccFlute?: boolean
}) {
  const store = useOilFieldStore()
  store.pipeStrategyFbeOriginal = !!opts.fbeOriginal
  store.pipeStrategyFbe = !!opts.fbe
  store.pipeStrategyConnectedCentersDelaunay = !!opts.ccDt
  store.pipeStrategyConnectedCentersDelaunayMst = !!opts.ccDtMst
  store.pipeStrategyConnectedCentersFlute = !!opts.ccFlute
}

function mockPerStrategy(responses: Partial<Record<PipeStrategy, string>>) {
  vi.mocked(wasmPlanner.plan).mockImplementation(async (json: string) => {
    const request = JSON.parse(json)
    const strategy = request.pipeStrategies[0] as PipeStrategy
    const response = responses[strategy]
    if (!response) {
      throw new Error(`no mock response for strategy ${strategy}`)
    }
    return response
  })
}

describe("getPlanWithProgress", () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it("returns the best result by effects, then beacons, then pipes", async () => {
    selectStrategies({ fbe: true, ccDt: true, ccFlute: true })
    mockPerStrategy({
      [PipeStrategy.Fbe]: planResponse(PipeStrategy.Fbe, { effects: 10, beacons: 5, pipes: 40 }, "FBE_BP"),
      [PipeStrategy.ConnectedCentersDelaunay]: planResponse(
        PipeStrategy.ConnectedCentersDelaunay,
        { effects: 12, beacons: 6, pipes: 50 },
        "CCDT_BP",
      ),
      [PipeStrategy.ConnectedCentersFlute]: planResponse(
        PipeStrategy.ConnectedCentersFlute,
        { effects: 12, beacons: 6, pipes: 45 },
        "CCFLUTE_BP",
      ),
    })

    const result = await getPlanWithProgress(() => {})

    expect(result.isError).toBe(false)
    if (!result.isError) {
      // CC-Flute wins: ties CC-DT on effects(12)/beacons(6) but has fewer pipes(45 < 50).
      expect(result.data.blueprint).toBe("CCFLUTE_BP")
    }
  })

  it("keeps the earliest strategy on an exact three-field tie", async () => {
    selectStrategies({ fbe: true, ccDt: true })
    const metrics: Metrics = { effects: 8, beacons: 4, pipes: 30 }
    mockPerStrategy({
      [PipeStrategy.Fbe]: planResponse(PipeStrategy.Fbe, metrics, "FIRST"),
      [PipeStrategy.ConnectedCentersDelaunay]: planResponse(
        PipeStrategy.ConnectedCentersDelaunay,
        metrics,
        "SECOND",
      ),
    })

    const result = await getPlanWithProgress(() => {})

    expect(result.isError).toBe(false)
    if (!result.isError) {
      expect(result.data.blueprint).toBe("FIRST")
    }
  })

  it("reports progress once per strategy plus a final tick", async () => {
    selectStrategies({ fbe: true, ccDt: true })
    mockPerStrategy({
      [PipeStrategy.Fbe]: planResponse(PipeStrategy.Fbe, { effects: 1, beacons: 1, pipes: 1 }, "A"),
      [PipeStrategy.ConnectedCentersDelaunay]: planResponse(
        PipeStrategy.ConnectedCentersDelaunay,
        { effects: 1, beacons: 1, pipes: 1 },
        "B",
      ),
    })

    const progress: PlanProgress[] = []
    await getPlanWithProgress((p) => progress.push({ ...p }))

    expect(progress.map((p) => p.current)).toEqual([0, 1, 2])
    expect(progress.every((p) => p.total === 2)).toBe(true)
    expect(progress[0].label).toBe("FBE*")
    expect(progress[1].label).toBe("CC-DT")
    expect(progress[2].label).toBe("Finalizing")
  })

  it("aborts on the first error and does not call later strategies", async () => {
    selectStrategies({ fbe: true, ccDt: true, ccFlute: true })
    vi.mocked(wasmPlanner.plan).mockImplementation(async (json: string) => {
      const request = JSON.parse(json)
      const strategy = request.pipeStrategies[0] as PipeStrategy
      if (strategy === PipeStrategy.ConnectedCentersDelaunay) {
        return errorResponse()
      }
      return planResponse(strategy, { effects: 1, beacons: 1, pipes: 1 }, "OK")
    })

    const result = await getPlanWithProgress(() => {})

    expect(result.isError).toBe(true)
    // FBE* then CC-DT only; CC-FLUTE is never reached.
    expect(vi.mocked(wasmPlanner.plan)).toHaveBeenCalledTimes(2)
  })

  it("falls back to a single call when no strategies are selected", async () => {
    selectStrategies({})
    vi.mocked(wasmPlanner.plan).mockResolvedValue(
      planResponse(PipeStrategy.Fbe, { effects: 1, beacons: 1, pipes: 1 }, "FALLBACK"),
    )

    const progress: PlanProgress[] = []
    const result = await getPlanWithProgress((p) => progress.push(p))

    expect(vi.mocked(wasmPlanner.plan)).toHaveBeenCalledTimes(1)
    expect(progress).toHaveLength(0)
    expect(result.isError).toBe(false)
  })
})
```

- [ ] **Step 4: Run the tests to verify they fail**

```bash
cd src/vue && npm run test
```

Expected: FAIL - `getPlanWithProgress` is not exported from `./OilFieldPlanner` (import error / "is not a function").

- [ ] **Step 5: Implement `getPlanWithProgress` and DRY `getPlan`**

In `src/vue/src/lib/OilFieldPlanner.ts`:

(a) Add `OilFieldPlan` to the existing import from `./FactorioToolsApi` (which already imports `PipeStrategy`, `OilFieldPlanRequest`, `OilFieldPlanResponse`, etc.):

```ts
import {
  BeaconStrategy,
  HttpResponse,
  OilFieldNormalizeRequest,
  OilFieldNormalizeResponse,
  OilFieldPlan,
  OilFieldPlanRequest,
  OilFieldPlanResponse,
  PipeStrategy,
} from "./FactorioToolsApi"
```

(b) Add the progress type, label map, ranking helper, and request builder near the top of the module body (e.g. just below the `ApiResult` interface). `OilFieldStoreState`, `useOilFieldStore`, `getEntries`, and `requestPropertyGetters` are already imported/defined in this file:

```ts
export type PlanProgress = { current: number; total: number; label: string }

// Display names mirror OilFieldPlan.ToString in the C# core.
const pipeStrategyLabels: Record<PipeStrategy, string> = {
  [PipeStrategy.FbeOriginal]: "FBE",
  [PipeStrategy.Fbe]: "FBE*",
  [PipeStrategy.ConnectedCentersDelaunay]: "CC-DT",
  [PipeStrategy.ConnectedCentersDelaunayMst]: "CC-DT-MST",
  [PipeStrategy.ConnectedCentersFlute]: "CC-FLUTE",
}

// Mirrors the planner's ranking: more beacon effects, then fewer beacons, then
// fewer pipes. Equal plans are NOT "strictly better", so the earliest strategy
// wins on an exact three-field tie.
function isStrictlyBetterPlan(candidate: OilFieldPlan, best: OilFieldPlan): boolean {
  if (candidate.beaconEffectCount !== best.beaconEffectCount) {
    return candidate.beaconEffectCount > best.beaconEffectCount
  }
  if (candidate.beaconCount !== best.beaconCount) {
    return candidate.beaconCount < best.beaconCount
  }
  return candidate.pipeCount < best.pipeCount
}

function buildPlanRequest(state: OilFieldStoreState): OilFieldPlanRequest {
  const request: OilFieldPlanRequest = { blueprint: "" }
  for (const [requestKey, getter] of getEntries(requestPropertyGetters)) {
    ;(request as unknown as Record<string, unknown>)[requestKey] = getter(state)
  }
  return request
}
```

(c) Replace the existing `getPlan` body to reuse `buildPlanRequest` (DRY), and add `getPlanWithProgress`:

```ts
export async function getPlan(): Promise<ApiResult<OilFieldPlanResponse> | ApiError> {
  const store = useOilFieldStore()
  const request = buildPlanRequest(store.$state)
  return await runWasm<OilFieldPlanResponse>(JSON.stringify(request), wasmPlanner.plan)
}

export async function getPlanWithProgress(
  onProgress: (progress: PlanProgress) => void,
): Promise<ApiResult<OilFieldPlanResponse> | ApiError> {
  const store = useOilFieldStore()
  const baseRequest = buildPlanRequest(store.$state)
  const strategies = baseRequest.pipeStrategies ?? []

  // No split possible: defer to the single combined call so behavior and
  // validation exactly match the non-progress path.
  if (strategies.length === 0) {
    return await runWasm<OilFieldPlanResponse>(JSON.stringify(baseRequest), wasmPlanner.plan)
  }

  const total = strategies.length
  let best: ApiResult<OilFieldPlanResponse> | null = null
  let bestPlan: OilFieldPlan | null = null

  for (let i = 0; i < total; i++) {
    const strategy = strategies[i]
    onProgress({ current: i, total, label: pipeStrategyLabels[strategy] })
    // Yield a macrotask so the browser paints the bar before the next blocking
    // WASM call (mirrors the paint yield in OilField.vue's invokeApi).
    await new Promise((resolve) => setTimeout(resolve, 0))

    const request: OilFieldPlanRequest = { ...baseRequest, pipeStrategies: [strategy] }
    const result = await runWasm<OilFieldPlanResponse>(JSON.stringify(request), wasmPlanner.plan)
    if (result.isError) {
      return result
    }

    const plan = result.data.summary.selectedPlans[0] ?? null
    if (
      best === null ||
      bestPlan === null ||
      (plan !== null && isStrictlyBetterPlan(plan, bestPlan))
    ) {
      best = result
      bestPlan = plan
    }
  }

  onProgress({ current: total, total, label: "Finalizing" })
  return best as ApiResult<OilFieldPlanResponse>
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
cd src/vue && npm run test
```

Expected: PASS - all 5 tests in `OilFieldPlanner.test.ts` green.

- [ ] **Step 7: Wire the tests into CI**

In `.github/workflows/ci.yml`, in the `build-vue` job, add a Test step immediately after the existing "Build" step (which uses the same `working-directory: ${{ env.BUILD_PATH }}`):

```yaml
      - name: Test
        run: npm run test
        working-directory: ${{ env.BUILD_PATH }}
```

- [ ] **Step 8: Commit**

```bash
git add src/vue/package.json src/vue/package-lock.json src/vue/vitest.config.ts \
        src/vue/src/lib/OilFieldPlanner.ts src/vue/src/lib/OilFieldPlanner.test.ts \
        .github/workflows/ci.yml
git commit -m "Add getPlanWithProgress with per-strategy progress and Vitest harness

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01T5pGHt1NTfdxGkcAGkfhQz"
```

---

### Task 2: UI wiring - `showProgress` setting, form toggle, and progress bar

**Files:**
- Modify: `src/vue/src/stores/OilFieldStore.ts` (add `showProgress` to `defaults` and `storeToQuery`)
- Modify: `src/vue/src/components/PlannerForm.vue` (advanced checkbox + store binding + reset)
- Modify: `src/vue/src/views/OilField.vue` (import, data, computed, submit branch, finally reset, progress bar markup)

**Interfaces:**
- Consumes: `getPlanWithProgress`, `PlanProgress`, `getPlan` from `../lib/OilFieldPlanner` (Task 1).
- Produces: a persisted boolean store field `showProgress` (default `false`); UI behavior only - no new exports other tasks depend on.

- [ ] **Step 1: Add the `showProgress` store field**

In `src/vue/src/stores/OilFieldStore.ts`, add to the `defaults` object (e.g. right after `addHeatPipes: false,`):

```ts
  showProgress: false,
```

And add the matching query-string key to `storeToQuery` (the mapped type requires every state key except `usingQueryString`/`useStagingApi`); place it after `addHeatPipes: "heatPipes",`:

```ts
  showProgress: "progress",
```

- [ ] **Step 2: Add the advanced toggle to PlannerForm**

In `src/vue/src/components/PlannerForm.vue` template, add a checkbox right after the `validate-solution` `form-check` block (before the `use-staging-api` block):

```html
    <div class="form-check">
      <input type="checkbox" class="form-check-input" id="show-progress" v-model="showProgress" />
      <label class="form-check-label" for="show-progress">Show planning progress</label> (slower;
      plans one strategy at a time so a progress bar can update)
    </div>
```

Add `"showProgress"` to the `pick(storeToRefs(useOilFieldStore()), ...)` list in `data()` (e.g. after `"validateSolution",`):

```ts
        "validateSolution",
        "showProgress",
```

Add the reset line in the `reset()` method (after `this.validateSolution = defaults.validateSolution`):

```ts
      this.showProgress = defaults.showProgress
```

- [ ] **Step 3: Import the progress API and type in the view**

In `src/vue/src/views/OilField.vue`, extend the existing import from `../lib/OilFieldPlanner`:

```ts
import {
  ApiError,
  ApiResult,
  getPlan,
  getPlanWithProgress,
  normalize,
  PlanProgress,
} from "../lib/OilFieldPlanner"
```

- [ ] **Step 4: Add progress state and computeds**

In `data()`, add `planProgress` to the first object literal (next to `submitting: false,`):

```ts
        submitting: false,
        planProgress: null as null | PlanProgress,
```

Add `"showProgress"` to the `pick(storeToRefs(useOilFieldStore()), ...)` list in `data()` (e.g. after `"addBeacons",`):

```ts
        "addBeacons",
        "showProgress",
```

Add a `computed` block to the component options (place it right before `methods: {`):

```ts
  computed: {
    progressPercent(): number {
      const p = this.planProgress
      if (!p || p.total === 0) {
        return 0
      }
      return Math.round((p.current / p.total) * 100)
    },
    progressStep(): number {
      const p = this.planProgress
      if (!p) {
        return 0
      }
      return Math.min(p.current + 1, p.total)
    },
  },
```

- [ ] **Step 5: Branch `submit()` and reset progress in `invokeApi`**

Replace the `getPlan()` call in `submit()` with a branch on `showProgress`:

```ts
    async submit() {
      await this.invokeApi(async () => {
        this.normalizeError = null
        const dataOrError = this.showProgress
          ? await getPlanWithProgress((p) => {
              this.planProgress = p
            })
          : await getPlan()
        if (dataOrError.isError) {
          this.plan = null
          this.planError = dataOrError
        } else {
          this.plan = dataOrError
          this.planError = null
        }
        return dataOrError
      })
    },
```

In `invokeApi`, also clear `planProgress` in the `finally` block:

```ts
      } finally {
        this.submitting = false
        this.planProgress = null
      }
```

- [ ] **Step 6: Add the progress bar markup**

In the template, add a Bootstrap progress bar immediately after the `</div>` that closes the `d-grid` submit-button wrapper (before `<OilFieldPlanView ... />`):

```html
    <div
      v-if="submitting && planProgress"
      class="progress mt-2"
      style="height: 1.5rem"
      role="progressbar"
      :aria-valuenow="progressPercent"
      aria-valuemin="0"
      aria-valuemax="100"
    >
      <div
        class="progress-bar progress-bar-striped progress-bar-animated"
        :style="{ width: progressPercent + '%' }"
      >
        {{ planProgress.label }} ({{ progressStep }} of {{ planProgress.total }})
      </div>
    </div>
```

- [ ] **Step 7: Type-check and run unit tests**

```bash
cd src/vue && npx vue-tsc --noEmit && npm run test
```

Expected: vue-tsc reports no errors; all unit tests still PASS (Task 1 logic unchanged).

- [ ] **Step 8: Manual verification with real WASM**

```bash
cd src/vue && npm run build-wasm && npm run build && npm run preview
```

Then in the browser at the preview URL:
- Enable "Show advanced options", confirm the new "Show planning progress" checkbox appears and is OFF by default.
- With it OFF: click "Plan oil field" - behavior is the spinner-only path as before; a plan is produced.
- With it ON: click "Plan oil field" - a determinate bar appears under the button, advancing in jumps ("FBE* (1 of 4)", etc.), and the final blueprint matches the OFF result for the same inputs.
- Confirm the bar disappears after completion (success and error paths).

Note: `npm run dev` does NOT run the WASM bundle - use `build` + `preview` (project CLAUDE.md / memory).

- [ ] **Step 9: Commit**

```bash
git add src/vue/src/stores/OilFieldStore.ts src/vue/src/components/PlannerForm.vue \
        src/vue/src/views/OilField.vue
git commit -m "Add opt-in planner progress bar to the UI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01T5pGHt1NTfdxGkcAGkfhQz"
```

---

## Notes on deferred work (not in this plan)

- Tier 2 (component tests via `@vue/test-utils` + `happy-dom`) and Tier 3 (Playwright e2e) are intentionally out of scope; the Vitest harness added in Task 1 supports adding them later without rework. (spec: Deferred)
- Cancel button, smooth/continuous bar, and Web Worker are explicit non-goals. (spec: Out of scope)
