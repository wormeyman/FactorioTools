# Quality support design

Date: 2026-06-25
Issue: https://github.com/joelverhagen/FactorioTools/issues/7 ("Add support for quality
pumpjacks, modules, and beacons")

## Summary

Add Factorio 2.0 quality (`uncommon`, `rare`, `epic`, `legendary`) to the oil-field planner as a
per-item-type option. Quality is applied to the output blueprint and, for electric poles only, feeds
the planner's geometry.

Two categories:

- **Output-only** (no change to planning): pumpjack quality, beacon quality, pumpjack-module
  quality, beacon-module quality. These stamp a `quality` string into the emitted 2.0 blueprint and
  nothing else. Because every entity of a given kind gets the same quality, the relative ranking of
  beacon plans is unchanged, so the optimization is unaffected (this matches the conclusion Joel and
  the requester reached on the issue: still maximize beacon effects).
- **Planner-affecting**: electric-pole quality. In Factorio 2.0 a higher-quality electric pole has a
  larger supply area and a longer wire reach, which changes how many poles the planner places and
  where. Pole quality is therefore not cosmetic and must adjust the planner inputs.

The blueprint output moves to the Factorio 2.0 format unconditionally. Today the planner emits 2.0
format only when `AddHeatPipes` is true and 1.1 otherwise; that conditional is removed and 2.0
becomes the only output format.

## Scope

In scope:

- Core options for five qualities (pumpjack, beacon, electric pole, pumpjack module, beacon module).
- Electric-pole quality scaling the planner's supply area and wire reach.
- 2.0 blueprint emission of entity quality and module quality; always-2.0 version/direction format.
- Vue UI quality selectors under the existing advanced-options toggle, rendered as CSS/SVG badges.
- CLI flags for the five qualities.
- Lua transpile parity (the Factorio mod emits quality too).

Out of scope:

- Mixed-quality input pumpjacks (different mining speeds would create a new optimization problem;
  explicitly deferred per the issue discussion).
- Per-individual-module quality (only one quality per entity's whole module set).
- Quality on pipes, underground pipes, heat pipes, or walls (always normal).
- Non-vanilla / modded quality scaling. The vanilla formula is assumed; modded users can set pole
  dimensions manually with `Normal` quality.

## Data model

New enum in the core library (`src/FactorioTools/OilField/`):

```csharp
public enum Quality
{
    Normal = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 5,
}
```

The integer value is the quality bonus level. Factorio skips a hidden level 4, so legendary is level
5 (this is why legendary's pole bonus is double epic's; see the formula below). A small helper maps
the enum to the blueprint string (`Normal -> "normal"`, ... `Legendary -> "legendary"`).

New fields on `OilFieldOptions`, all defaulting to `Quality.Normal`:

- `PumpjackQuality`
- `BeaconQuality`
- `ElectricPoleQuality`
- `PumpjackModuleQuality`
- `BeaconModuleQuality`

The module sets stay `Dictionary<string,int>` (item name -> count). The per-set quality field
applies to that entity's whole module set. This keeps the dictionary shape unchanged (good for Lua
and backward compatibility) and satisfies the chosen per-item-type granularity. The richer
per-module-entry quality option was considered and rejected as unnecessary.

Because the API request DTO (`OilFieldPlanRequestResponse`) inherits `OilFieldOptions`, the new
fields flow automatically into swagger and the generated Vue TypeScript client.

## Electric-pole quality and planner geometry

A higher-quality electric pole enlarges supply area and wire reach. The scaling is a single
universal formula (verified against the wiki for small, medium, and substation poles):

```
level = (int)ElectricPoleQuality                         // 0, 1, 2, 3, 5
effectiveSupplyWidth  = ElectricPoleSupplyWidth  + 2 * level
effectiveSupplyHeight = ElectricPoleSupplyHeight + 2 * level
effectiveWireReach    = ElectricPoleWireReach    + 2 * level
```

Verification data (wiki):

| Pole       | Supply (normal -> legendary)          | Wire reach (normal -> legendary)        |
| ---------- | ------------------------------------- | --------------------------------------- |
| Small      | 5 -> 7 -> 9 -> 11 -> 15               | 7.5 -> 9.5 -> 11.5 -> 13.5 -> 17.5      |
| Medium     | 7 -> 9 -> 11 -> 13 -> 17             | 9 -> 11 -> 13 -> 15 -> 19               |
| Substation | 18 -> 20 -> 22 -> 24 -> 28           | 18 -> 20 -> 22 -> 24 -> 28              |

The big electric pole follows the same formula and will be confirmed against the wiki during
implementation. The pole footprint (`ElectricPoleWidth`/`ElectricPoleHeight`) is unchanged by
quality.

The supplied supply/wire option values are treated as the normal-quality base. The bonus is added in
exactly one place in the core (during context initialization / option normalization, e.g. in
`InitializeContext` or the orchestrator before planning) so the CLI, WASM front-end, and Lua mod all
behave identically. The planner already consumes `ElectricPoleSupplyWidth/Height` and
`ElectricPoleWireReach`, so no algorithm change is required beyond computing the effective values.

Decision: the Vue advanced inputs keep showing the base (normal-quality) supply/wire numbers, and the
core adds the bonus. The UI shows a small derived "effective coverage" hint next to the pole-quality
selector so the user sees the result. (The alternative - having the UI compute and send effective
numbers - was rejected to keep a single source of truth in the core and avoid double-counting.)

## Blueprint emission

Changes in the serialization project (`GridToBlueprintString.cs`, `EntityItemsConverter.cs`) and the
core `Entity` model (`src/FactorioTools/Data/Entity.cs`):

- Version: always emit 2.0 (`FormatVersion(2, 0, ...)`); drop the `AddHeatPipes ? 2.0 : 1.1`
  conditional. Directions always use the 2.0 16-way encoding (N=0, E=4, S=8, W=12). The 2.0 module
  `items` array form becomes the only module emission path.
- Entity quality: add a nullable `Quality` string property to `Entity`, omitted when null. Stamp it
  on pumpjack, beacon, and electric-pole entities from the corresponding option. Omit when the
  quality is `Normal`.
- Module quality: `ModuleInsertPlan` gains a `Quality` string. The converter writes it inside the
  module `id` object: `"id": { "name": "speed-module-3", "quality": "legendary" }`. Omit when normal.
- Pipes, underground pipes, heat pipes, and walls are always normal quality (no quality field).

## Vue UI

Quality controls live under the existing `useAdvancedOptions` toggle.

- New reusable `QualitySelect.vue` component (mirrors `ModuleSelect.vue`), rendering each quality as a
  CSS/SVG badge: a small chevron/pip cluster in the official quality color with the quality name.
  Colors approximate the in-game palette - normal (white/grey), uncommon (green), rare (blue), epic
  (purple), legendary (gold/orange); exact hex values pinned from the game/wiki during
  implementation. No copyrighted PNG assets are bundled; the app stays asset-free.
- Placement:
  - `PumpjacksForm.vue`: pumpjack quality + pumpjack-module quality.
  - `BeaconForm.vue`: beacon quality + beacon-module quality.
  - `ElectricPoleForm.vue`: pole quality, with the derived effective-coverage hint.
- Store (`OilFieldStore.ts`): new fields with short persisted URL keys (e.g. `pumpQ`, `pumpModQ`,
  `beaconQ`, `beaconModQ`, `poleQ`), defaulting to `normal` so existing URLs and behavior are
  unchanged.
- Request mapping (`OilFieldPlanner.ts`): new entries in `requestPropertyGetters` for the five
  qualities.
- After the C# change, run `npm run build-wasm` so the deployed WASM bundle includes quality.

## CLI

Expose the five qualities on the `oil-field` command(s) that build options, as flags such as
`--pumpjack-quality`, `--beacon-quality`, `--electric-pole-quality`, `--pumpjack-module-quality`,
`--beacon-module-quality`, each defaulting to `normal`. Primarily for scripting and test parity.

## Lua transpile parity

The enum, the string mapping, and the pole formula are plain arithmetic and string handling - Lua
safe (no LINQ, `yield return`, try/catch, or struct dictionary keys). Regenerate `src/lua` via
`Invoke-LuaBuild.ps1` and syntax-check with `luac5.2`. The transpiled output (used by the Factorio
mod) emits quality too. The `pump` mod (`pump_2.2.0/`) is prior art for quality handling in a
Factorio-mod context and can be consulted, but it is not a source of icons or formulas.

## Testing

- Output is now always 2.0, so all blueprint `*.verified.txt` snapshots regenerate (directions and
  module-items format change). This is expected; accept the regenerated snapshots via the Verify
  workflow.
- New unit tests:
  - Quality enum -> blueprint string mapping.
  - Pole scaling formula: assert the medium and substation rows from the table above.
  - Emission: `quality` present/omitted correctly on pumpjack, beacon, and electric-pole entities.
  - Module emission: `id.quality` shape present/omitted correctly.
- Build and test under the default configuration and under `UseLuaSettings=true`.

## Risks and notes

- Always-2.0 output changes the format for all users (1.1 importers can no longer use the output).
  Accepted: Factorio 2.0 is current and 1.1 is legacy.
- Snapshot churn is large but mechanical.
- The big-pole scaling row and the exact quality color hex values are the two factual details to pin
  during implementation; everything else is verified.
