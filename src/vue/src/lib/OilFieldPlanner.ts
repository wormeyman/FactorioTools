import { OilFieldStoreState, useOilFieldStore } from "../stores/OilFieldStore"
import {
  BeaconStrategy,
  HttpResponse,
  OilFieldNormalizeRequest,
  OilFieldNormalizeResponse,
  OilFieldPlanRequest,
  OilFieldPlanResponse,
  PipeStrategy,
} from "./FactorioToolsApi"
import { getEntries } from "./helpers"
import * as wasmPlanner from "./wasmPlanner"
import { PlanCancelledError } from "./wasmPlanner"

type RequestPropertyGetters = {
  [Property in keyof OilFieldPlanRequest]-?: (
    state: OilFieldStoreState,
  ) => Exclude<OilFieldPlanRequest[Property], undefined>
}

const requestPropertyGetters: RequestPropertyGetters = {
  addBeacons: (state) => state.addBeacons,
  addElectricPoles: (state) => state.addElectricPoles,
  addFbeOffset: (_) => false,
  addHeatPipes: (state) => state.addHeatPipes,
  beaconEntityName: (state) => state.beaconEntityName.trim(),
  beaconHeight: (state) => state.beaconHeight,
  beaconModules: (state) => {
    const output: Record<string, number> = {}
    const module = state.beaconModule.trim()
    if (module) {
      output[module] = state.beaconModuleSlots
    }
    return output
  },
  beaconStrategies: (state) =>
    [
      state.beaconStrategyFbeOriginal ? BeaconStrategy.FbeOriginal : undefined,
      state.beaconStrategyFbe ? BeaconStrategy.Fbe : undefined,
      state.beaconStrategySnug ? BeaconStrategy.Snug : undefined,
    ].filter((b): b is BeaconStrategy => !!b),
  beaconSupplyHeight: (state) => state.beaconSupplyHeight,
  beaconSupplyWidth: (state) => state.beaconSupplyWidth,
  beaconWidth: (state) => state.beaconWidth,
  blueprint: (state) => state.inputBlueprint.trim(),
  electricPoleEntityName: (state) => state.electricPoleEntityName.trim(),
  electricPoleHeight: (state) => state.electricPoleHeight,
  electricPoleSupplyHeight: (state) => state.electricPoleSupplyHeight,
  electricPoleSupplyWidth: (state) => state.electricPoleSupplyWidth,
  electricPoleWidth: (state) => state.electricPoleWidth,
  electricPoleWireReach: (state) => state.electricPoleWireReach,
  heatPipeEntityName: (_) => "heat-pipe",
  optimizePipes: (state) => state.optimizePipes,
  overlapBeacons: (state) => state.overlapBeacons,
  pipeStrategies: (state) =>
    [
      state.pipeStrategyFbeOriginal ? PipeStrategy.FbeOriginal : undefined,
      state.pipeStrategyFbe ? PipeStrategy.Fbe : undefined,
      state.pipeStrategyConnectedCentersDelaunay
        ? PipeStrategy.ConnectedCentersDelaunay
        : undefined,
      state.pipeStrategyConnectedCentersDelaunayMst
        ? PipeStrategy.ConnectedCentersDelaunayMst
        : undefined,
      state.pipeStrategyConnectedCentersFlute ? PipeStrategy.ConnectedCentersFlute : undefined,
    ].filter((b): b is PipeStrategy => !!b),
  pumpjackModules: (state) => {
    const output: Record<string, number> = {}
    const module = state.pumpjackModule.trim()
    if (module) {
      output[module] = 2
    }
    return output
  },
  useUndergroundPipes: (state) => state.useUndergroundPipes,
  validateSolution: (state) => state.validateSolution,
} as const

export type ApiError = {
  isError: true
  title: string
  errors?: Record<string, string[]>
  errorDetails?: string[]
  response?: HttpResponse<unknown, unknown>
}

export interface ApiResult<Data> {
  isError: false
  data: Data
}

async function runWasm<Data>(
  requestJson: string,
  invoke: (json: string) => Promise<string>,
): Promise<ApiResult<Data> | ApiError> {
  try {
    const responseJson = await invoke(requestJson)
    const parsed = JSON.parse(responseJson)

    // Error envelope shape: { title, status, errors }
    if (
      parsed &&
      typeof parsed === "object" &&
      "status" in parsed &&
      "errors" in parsed &&
      !("blueprint" in parsed)
    ) {
      const errors: Record<string, string[]> = {}
      if (parsed.errors && typeof parsed.errors === "object") {
        for (const [key, values] of Object.entries(parsed.errors)) {
          if (Array.isArray(values)) {
            errors[key] = (values as unknown[]).filter((v): v is string => typeof v === "string")
          }
        }
      }
      return { isError: true, title: parsed.title ?? "An error occurred.", errors }
    }

    return { isError: false, data: parsed as Data }
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
}

export async function normalize(): Promise<ApiResult<OilFieldNormalizeResponse> | ApiError> {
  const store = useOilFieldStore()
  const request: OilFieldNormalizeRequest = { blueprint: store.$state.inputBlueprint }
  return await runWasm<OilFieldNormalizeResponse>(JSON.stringify(request), wasmPlanner.normalize)
}

export async function getPlan(): Promise<ApiResult<OilFieldPlanResponse> | ApiError> {
  const store = useOilFieldStore()
  const request: OilFieldPlanRequest = { blueprint: "" }
  for (const [requestKey, getter] of getEntries(requestPropertyGetters)) {
    ;(request as unknown as Record<string, unknown>)[requestKey] = getter(store.$state)
  }
  return await runWasm<OilFieldPlanResponse>(JSON.stringify(request), wasmPlanner.plan)
}
