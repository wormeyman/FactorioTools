# In-browser WASM planner on Cloudflare Pages

Date: 2026-06-18
Status: Approved design (pending implementation plan)
Branch: `cloudflare-wasm-deploy`

## Goal

Serve the FactorioTools oil-field planner as a fully static site on Cloudflare
Pages, with all planning running in the visitor's browser via the existing
`BrowserWasm` (.NET WebAssembly) build. No server and no container run in
production.

## Background and motivation

The deployed Vue app does **not** plan in the browser today. `src/vue/src/lib/OilFieldPlanner.ts`
sends both `normalize` and `plan` requests to the Azure-hosted ASP.NET API
(`https://factoriotools.azurewebsites.net`). The GitHub Pages deploy workflow
only installs Node and runs the Vue build, so the `BrowserWasm` project is a
capability that exists but is not wired into the deployed app.

The planner is a stateless pure function (blueprint string + options in,
blueprint string + plan summary out): no shared state, no auth, no database.
That makes it an ideal candidate to run client-side, which eliminates the
server entirely - zero hosting cost, no per-request cold start, nothing to
patch or scale.

## Non-goals

- Rewriting the planner in JavaScript/TypeScript. We reuse the compiled .NET
  core via WASM.
- Deleting the `WebApp` project. It remains the source of truth for the
  swagger contract / generated TypeScript types and a working API if ever
  wanted.
- Migrating off Azure/Vercel/GitHub Pages in this change. Retiring those
  deploys is a reversible follow-up after Cloudflare is validated.
- Heat-pipe / Aquilo work (tracked separately on `heat-pipes-aquilo`).

## Decisions

1. **Shared contract project.** Extract the request/response DTOs and the plan
   and normalize orchestration into a new `FactorioTools.Contract` project,
   consumed by both `WebApp` and `BrowserWasm`. DRY: one orchestration, two
   hosts, one contract. This project is **not** added to the Lua build, so Lua
   transpilation is unaffected.
2. **AOT disabled.** Set `RunAOTCompilation=false` in `BrowserWasm.csproj` for
   a smaller download and faster CI builds. Planning runs on the IL
   interpreter, which is slower; this is the easiest knob to flip back if
   interpreted planning is too slow in practice.
3. **JSON-string marshaling with source-generated serialization.** The WASM
   exports take a JSON request string and return a JSON response string,
   matching the existing API request/response shapes exactly. Because
   `BrowserWasm` publishes with `PublishTrimmed=true` / `TrimMode=full`,
   reflection-based `System.Text.Json` is not trim-safe, so `Contract` defines a
   source-generated `JsonSerializerContext` for all DTOs. Enum parity matters:
   `WebApp` registers `JsonStringEnumConverter`, and the generated TS sends enum
   values in their declared **PascalCase** (`"FbeOriginal"`,
   `"ConnectedCentersDelaunay"`, `"Snug"`) with **camelCase** property names. The
   WASM serializer must reproduce exactly that - trim-safe
   `JsonStringEnumConverter<T>` keeping the declared (PascalCase) casing plus
   camelCase properties, **not** camelCase enum values - so the two hosts are
   byte-for-byte compatible. A parity test guards this.
4. **Direct Upload deploy.** A GitHub Actions workflow builds the WASM and the
   Vue site, then uploads the static output to the Cloudflare Pages project
   **`factoriotools`** (served at `factoriotools-5jg.pages.dev`, root path) via
   `wrangler pages deploy`. We do not use Cloudflare's Git-integration build
   because that build image has no .NET toolchain. Serving at the Pages root
   keeps Vite `base` at `/` and lets `dotnet.js` resolve its sibling assets at
   the root. The bundle directory is named `framework`, not the .NET default
   `_framework`: Cloudflare Pages strips leading-underscore directories on
   deploy (serving their contents from the root), which 404s a
   `_framework/dotnet.js` import. (Found post-launch; see CLAUDE.md.)
5. **Node 24 LTS.** The Node 24 (Active LTS) bump already landed on `main`:
   `.github/workflows/ci.yml`, `.github/workflows/deploy-pages.yml`
   (`node-version`), and `src/vue/package.json` (`@types/node`). Remaining for
   this work: the new `deploy-cloudflare.yml` (Decision 4) targets Node 24, and
   the `CLAUDE.md` prerequisites note still reads "Node 18 for the Vue
   front-end" and needs updating to Node 24.

## Architecture

```
Before:  Vue (static, GH Pages/Vercel)  --HTTP-->  ASP.NET API (Azure)
After:   Vue (static, Cloudflare Pages)  --in-process JS->WASM-->  .NET planner
```

### Components and boundaries

**`FactorioTools.Contract` (new project)**
- References `FactorioTools` (core) and `FactorioTools.Serialization`. No
  dependency on `Microsoft.AspNetCore.*` or any browser API.
- Owns **all six data DTOs** moved out of `WebApp.Models`:
  `OilFieldPlanRequestResponse` (derives from `OilFieldOptions`),
  `OilFieldPlanRequest`, `OilFieldPlanResponse`,
  `OilFieldNormalizeRequestResponse`, `OilFieldNormalizeRequest`,
  `OilFieldNormalizeResponse`. The `*RequestResponse` base types carry the data
  (`Blueprint`, `AddFbeOffset`) that the requests derive from and the responses
  reference, so they must move too. Only the two `ISchemaFilter` implementations
  stay in `WebApp` (they depend on Swashbuckle/OpenApi):
  `OilFieldPlanRequestDefaultsSchemaFilter` and
  `RequireNonNullablePropertiesSchemaFilter<T>`; they reference the moved DTO
  types via updated `using`s. Swagger schema names derive from type names (not
  namespaces), so `swagger.json` is unchanged - but `Contract` must set
  `GenerateDocumentationFile=true` because `Program.cs` pulls DTO XML docs via
  `typeof(OilFieldPlanRequest).Assembly`.
- Owns a plain error-envelope DTO that mirrors the JSON the API's
  `ExceptionFilter` produces today (`title`, `status`, and an `errors`
  dictionary keyed by `FactorioToolsException`). It is a POCO and must **not**
  use `Microsoft.AspNetCore.Mvc.ProblemDetails`, which is unavailable in WASM.
- Owns a source-generated `JsonSerializerContext` registering all request,
  response, and error DTOs, with camelCase property naming and trim-safe
  string-enum converters (Decision 3).
- Owns `PlanOrchestrator`, two surfaces over one shared pipeline
  (`ParseBlueprint` -> `Planner.Execute` / `CleanBlueprint` ->
  `GridToBlueprintString`):
  - **Typed, throwing** - `Plan(OilFieldPlanRequest) -> OilFieldPlanResponse`
    and `Normalize(...)`. They let `FactorioToolsException` propagate. `WebApp`
    uses these, so its existing `ExceptionFilter` (HTTP 400/500 + ProblemDetails)
    is untouched.
  - **JSON string, catching** - `PlanJson(string) -> string` /
    `NormalizeJson(string) -> string`. They deserialize with the source-gen
    context, call the typed method, catch `FactorioToolsException` (honoring its
    `BadInput` flag), and serialize either the success response or the
    error-envelope DTO. The WASM host uses these; the JS boundary has no HTTP
    status, so success/error is encoded in the returned JSON.
- What it does: run the planner pipeline and (de)serialize at the JS boundary.
  What it depends on: core + serialization only.
- Not added to `src/lua/Invoke-LuaBuild.ps1` inputs.

**`WebApp` (modified)**
- `OilFieldController.NormalizeBlueprint` / `GetPlan` collapse to one-liners
  calling the **typed** `PlanOrchestrator` methods, still returning the typed
  DTOs - so model binding, the existing `ExceptionFilter` (status codes +
  ProblemDetails), and ASP.NET JSON serialization are all unchanged.
- The DTOs change namespace (from `Knapcode.FactorioTools.WebApp.Models` to the
  `Contract` namespace); `WebApp` updates its `using`s, and the schema filters
  that stay behind reference the moved types. swagger.json output is unchanged,
  so `src/vue/src/lib/FactorioToolsApi.ts` generated types stay identical.

**`BrowserWasm` (modified)**
- `RunAOTCompilation=false` (Decision 2). `PublishTrimmed` / `TrimMode=full`
  stay on - trim-safety comes from the source-gen serializer, not from
  disabling trim.
- `Program.cs`: replace the demo `Greeting()` with
  `[JSExport] string Plan(string requestJson)` and
  `[JSExport] string Normalize(string requestJson)`, each delegating to
  `PlanOrchestrator.PlanJson` / `.NormalizeJson`.
- Add a `ProjectReference` to `FactorioTools.Contract`.

**`src/vue` front-end (modified)**
- New `src/lib/wasmPlanner.ts`: boots the `dotnet.js` runtime once behind a
  singleton promise, resolves the assembly exports, and exposes
  `plan(requestJson)` / `normalize(requestJson)`. Boots lazily on first use,
  with a loading indicator in the UI.
- The WASM exports run **synchronously on the main thread** (AOT off = IL
  interpreter, ~10-30x slower than desktop JIT). Two consequences: (1) boot
  lazily behind a loading indicator; (2) before each synchronous call, set the
  loading state and yield to the browser (`await new Promise(r =>
  setTimeout(r, 10))` - a small non-zero delay is more reliable than `0` for
  guaranteeing a paint cycle across mobile/desktop browsers) so the spinner
  paints before the UI thread blocks.
- `src/lib/OilFieldPlanner.ts`: replace the two calls to the generated `Api`
  client (`new Api({ baseUrl }).*`, which currently target the staging/prod
  Azure URLs selected by the `useStagingApi` store toggle) with `wasmPlanner`
  calls. The request objects and response types are unchanged. The error path in
  `getApiResultOrError` adapts from the `e instanceof Response` branch to parsing
  the JSON string WASM returns: a success payload maps to `ApiResult`, an error
  envelope maps to the existing `ApiError` (`{ isError, title, errors }`); the
  optional `response` (`HttpResponse`) field is simply absent on the WASM path.
- The published WASM output (the `_framework/*` bundle incl. `dotnet.js`) is
  copied into `src/vue/public/framework/` so Vite ships it in `dist`. Served at
  the Pages root, the loader resolves `framework/dotnet.js` and its siblings at
  `/` (Vite `base` `/`). The directory is renamed away from the underscore so
  Cloudflare Pages does not strip it (see the deploy decision above).

### Data flow (plan request)

1. User edits options + pastes blueprint in the Vue UI (Pinia store).
2. `OilFieldPlanner.getPlan()` builds the `OilFieldPlanRequest` object (existing
   `requestPropertyGetters`), `JSON.stringify`s it.
3. `wasmPlanner.plan(json)` ensures the runtime is booted, calls the
   `[JSExport] Plan` export.
4. WASM `PlanOrchestrator.Plan` parses, plans, serializes, returns response JSON
   (or structured error JSON).
5. Front-end parses the JSON into the existing `OilFieldPlanResponse` /
   `ApiError` shapes and renders.

### Error handling

- `WebApp`: unchanged. The typed orchestrator methods throw
  `FactorioToolsException`; the existing `ExceptionFilter` turns it into the
  ProblemDetails response (HTTP 400 for `BadInput`, otherwise 500).
- `WASM`: `PlanJson` / `NormalizeJson` catch `FactorioToolsException` and return
  the error-envelope DTO as JSON (same `title` / `errors`, with `status` derived
  from the bad-input flag). The front-end's `ApiError` consumers keep working via
  a thin adapter that reads this JSON instead of an HTTP `Response`.

## Build and deploy

New workflow `deploy-cloudflare.yml`:

1. `actions/checkout` with `submodules: recursive`.
2. `actions/setup-dotnet` honoring `global.json` (.NET 8) + `dotnet workload restore`
   (wasm-tools).
3. `dotnet publish src/BrowserWasm -c Release` (AOT off).
4. Copy the publish `_framework` bundle (incl. `dotnet.js`) into
   `src/vue/public/framework/` (renamed to drop the leading underscore).
5. `actions/setup-node` (Node 24), `npm install`, `npm run build` in `src/vue`.
6. `wrangler pages deploy src/vue/dist --project-name=factoriotools` using a
   Cloudflare API token stored as a GitHub secret.

One-time setup (handled during implementation): provision the `factoriotools`
Cloudflare Pages project via the Cloudflare MCP / `wrangler`, then add the
`CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID` GitHub secrets. Creating the
scoped API token in the Cloudflare dashboard and pasting it into GitHub secrets
is the only step the maintainer does by hand.

### Local development

`vite dev` serves `src/vue/public/` at the root, but the WASM assets there go
stale whenever the C# planner changes. Add an `npm run build-wasm` script that
publishes `BrowserWasm` (Debug) and copies the published framework output into
`src/vue/public/`, and document the "re-run after C# edits" step in `CLAUDE.md`.
The exact publish output path is pinned during implementation against the actual
`dotnet publish` layout (rather than guessed).

## Testing

- **Contract unit tests:** new tests for both orchestrator surfaces - the typed
  `Plan` / `Normalize` (happy path; `FactorioToolsException` propagates) and the
  JSON `PlanJson` / `NormalizeJson` (happy path; bad-input blueprint asserting
  the error-envelope JSON). Add a **serialization parity** test asserting the
  source-gen context produces JSON identical to WebApp's options for a known
  request - especially PascalCase enum values and camelCase properties. These
  run under the existing xUnit project and the standard build flag matrix.
- **Existing tests:** the controller behavior is preserved via delegation;
  existing planner/serialization tests are unaffected.
- **Manual verification:** build the WASM + Vue locally (or via the Docker dev
  loop for the .NET parts), load the site, plan a sample blueprint, confirm
  output matches the current API for the same input.

## Risks and verification

- **WASM payload size:** even without AOT, verify the published `.wasm` files
  sit under Cloudflare Pages' ~25 MiB per-file limit and the total file count is
  well under Pages' 20,000-file limit. Check after the first publish.
- **Interpreted planning speed:** with AOT off, planning is slower. Measure on a
  representative blueprint; if unacceptable, re-enable AOT (decision 2) and
  re-check size.
- **First-load boot:** ~1-2s one-time runtime init in the browser, then cached.
  Acceptable for a tool opened deliberately; surface a loading state.
- **Asset base path:** resolved by serving at the Pages root (Vite `base` `/`),
  so `dotnet.js` resolves its sibling assets at the root. This avoids the GitHub
  Pages project-subpath problem that would otherwise need custom loader config.
  Note: the bundle ships under `framework/`, not `_framework/` - Cloudflare Pages
  strips leading-underscore directories, which 404s a `_framework/dotnet.js`
  import (found post-launch and fixed by renaming the directory).
- **Determinism:** the WASM host runs the same compiled core as the API, so plan
  output should be identical for identical input; the parity test guards this.

## Security

- Moving planning to the client removes all server-side secrets, auth, and
  database surface.
- `ParseBlueprint.Execute` validates input and throws structured
  `FactorioToolsException`s that `PlanJson` / `NormalizeJson` catch - malformed
  blueprints yield an error envelope, not a crash.
- The single `v-html` sink (`OilField.vue`, step descriptions from `steps.ts`)
  renders hardcoded constants, not user input - no XSS exposure.

## Open follow-ups (out of scope here)

- Retire the Azure / Vercel / GitHub Pages deploy workflows once Cloudflare is
  validated.
- Optionally keep the API as a non-default fallback (the store already has a
  `useStagingApi` toggle).
