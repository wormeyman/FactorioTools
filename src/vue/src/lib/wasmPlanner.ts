// Boots the .NET WASM runtime once and exposes the Plan/Normalize exports.
// The bundle is published into /public by `npm run build-wasm` and served at the
// site root. For this Exe-style WASM bundle, dotnet.js ships inside `framework`.
// NOTE: the directory is deliberately NOT named `_framework` - Cloudflare Pages
// strips leading-underscore directories and serves their contents from the root,
// which 404s the `_framework/dotnet.js` import.

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
