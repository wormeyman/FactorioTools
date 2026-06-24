// Boots the .NET WASM runtime once and exposes the Plan/Normalize exports.
// The bundle is published into public/framework by `npm run build-wasm` and
// served at the site root, so dotnet.js lives at `framework/dotnet.js` and
// resolves its sibling assets relative to itself.
//
// The runtime is booted and invoked on the main thread on purpose: the
// single-threaded .NET WASM runtime deadlocks during `create()` when booted in
// a dedicated Web Worker (it is designed to initialize on the browser's main
// thread). Running it in a worker would require the multithreaded build plus
// SharedArrayBuffer and COOP/COEP cross-origin-isolation headers.

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
      // Resolve dotnet.js relative to the deployed base (root '/').
      const dotnetUrl = `${__BASE_PATH__}framework/dotnet.js`
      const { dotnet } = await import(/* @vite-ignore */ dotnetUrl)
      const { getAssemblyExports, getConfig } = await dotnet.withDiagnosticTracing(false).create()
      const config = getConfig()
      return (await getAssemblyExports(config.mainAssemblyName)) as Exports
    })()
  }
  return exportsPromise
}

export async function plan(requestJson: string): Promise<string> {
  const exports = await bootExports()
  return exports.Interop.Plan(requestJson)
}

export async function normalize(requestJson: string): Promise<string> {
  const exports = await bootExports()
  return exports.Interop.Normalize(requestJson)
}
