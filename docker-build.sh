#!/usr/bin/env bash
#
# Run dotnet inside the pinned .NET 8 SDK container, so you can build and test
# without installing the .NET 8 SDK locally (handy on machines that only have a
# newer SDK than the global.json pin). Works with OrbStack or Docker Desktop.
#
# Usage: ./docker-build.sh [dotnet args...]
#   ./docker-build.sh                     # default: test the core dev loop
#   ./docker-build.sh build               # build the solution (needs wasm-tools, see note)
#   ./docker-build.sh build src/FactorioTools/FactorioTools.csproj -c Release /p:UseLuaSettings=true
#   ./docker-build.sh run --project src/FactorioTools.Cli -- oil-field sample
#
# Notes:
# - Git submodules are read from your checkout; run `git submodule update --init
#   --recursive` first if they are missing.
# - A full-solution `build` pulls in BrowserWasm/BlazorWebApp, which need the
#   wasm-tools workload. Prepend a workload restore for that, e.g.:
#     ./docker-build.sh bash -c "dotnet workload restore && dotnet build -c Release"
# - NuGet packages are cached on the host (override with FACTORIO_NUGET_CACHE).
# - Runs as the current user so build artifacts in the tree are not root-owned.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NUGET_CACHE="${FACTORIO_NUGET_CACHE:-$HOME/.cache/factorio-nuget}"
SDK_IMAGE="${FACTORIO_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:8.0}"

mkdir -p "$NUGET_CACHE"

# Default to the core dev loop (core lib + serialization + Verify snapshots).
if [ "$#" -eq 0 ]; then
  set -- test test/FactorioTools.Test/FactorioTools.Test.csproj -c Release
fi

# If the first arg is a literal program (e.g. `bash`), run it as-is; otherwise
# treat the args as a `dotnet` subcommand.
case "${1:-}" in
  bash|sh) ;;
  *) set -- dotnet "$@" ;;
esac

exec docker run --rm \
  --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e NUGET_PACKAGES=/nuget \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  -v "$REPO_ROOT":/src -w /src \
  -v "$NUGET_CACHE":/nuget \
  "$SDK_IMAGE" \
  "$@"
