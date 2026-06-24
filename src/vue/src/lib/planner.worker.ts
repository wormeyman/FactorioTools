// Runs the .NET WASM planner off the main thread. Boots the runtime once, then answers
// plan/normalize messages posted by wasmPlanner.ts. A single, non-threaded dedicated worker -
// no SharedArrayBuffer and no cross-origin-isolation headers required.

/// <reference lib="webworker" />
export {}

type Exports = {
  Interop: {
    Plan: (requestJson: string) => string
    Normalize: (requestJson: string) => string
  }
}

let exportsPromise: Promise<Exports> | null = null

function bootExports(): Promise<Exports> {
  if (!exportsPromise) {
    exportsPromise = (async () => {
      const dotnetUrl = `${__BASE_PATH__}framework/dotnet.js`
      const { dotnet } = await import(/* @vite-ignore */ dotnetUrl)
      const { getAssemblyExports, getConfig } = await dotnet.withDiagnosticTracing(false).create()
      const config = getConfig()
      return (await getAssemblyExports(config.mainAssemblyName)) as Exports
    })()
  }
  return exportsPromise
}

self.onmessage = async (e: MessageEvent) => {
  const { id, op, requestJson } = e.data as {
    id: number
    op: "plan" | "normalize"
    requestJson: string
  }
  try {
    const exports = await bootExports()
    const responseJson =
      op === "plan" ? exports.Interop.Plan(requestJson) : exports.Interop.Normalize(requestJson)
    self.postMessage({ id, responseJson })
  } catch (err) {
    self.postMessage({
      id,
      error: err instanceof Error ? (err.stack ?? err.message) : String(err),
    })
  }
}
