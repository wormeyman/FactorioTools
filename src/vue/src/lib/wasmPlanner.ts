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
