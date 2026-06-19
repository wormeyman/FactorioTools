# In-browser WASM Planner on Cloudflare Pages - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the FactorioTools oil-field planner entirely in the visitor's browser via .NET WASM, served as a static Cloudflare Pages site, with no server.

**Architecture:** Extract the request/response DTOs and a `PlanOrchestrator` into a new `FactorioTools.Contract` project shared by `WebApp` and `BrowserWasm`. The orchestrator has two surfaces: typed+throwing (WebApp keeps its `ExceptionFilter`) and JSON-string+catching (the WASM `[JSExport]`s). The Vue app calls the WASM exports in-process instead of the Azure API. A GitHub Actions workflow publishes the WASM + Vue build to Cloudflare Pages via `wrangler`.

**Tech Stack:** .NET 8 (built via `./docker-build.sh`), System.Text.Json source generators, Vue 3 + Vite 7 + TypeScript, Cloudflare Pages + wrangler, GitHub Actions.

## Global Constraints

- **.NET build/test only via `./docker-build.sh`** (no local .NET 8 SDK; OrbStack/Docker required). Core dev loop: `./docker-build.sh test test/FactorioTools.Test/FactorioTools.Test.csproj -c Release`.
- **`FactorioTools.Contract` is NOT added to the Lua build** (`src/lua/Invoke-LuaBuild.ps1`). Do not add System.Text.Json attributes to core (`FactorioTools`) types - the core is Lua-transpiled.
- **Node 24 LTS** for all front-end / CI Node steps.
- **Cloudflare Pages project name: `factoriotools`** (served at `factoriotools.pages.dev`, root path, Vite `base` `/`).
- **swagger.json must stay byte-identical** after the DTO move, so `src/vue/src/lib/FactorioToolsApi.ts` regenerates unchanged.
- **Enum/JSON parity:** front-end sends camelCase properties and PascalCase enum values (`"FbeOriginal"`, `"ConnectedCentersDelaunay"`, `"Snug"`). WASM serialization must match WebApp's `new JsonSerializerOptions(JsonSerializerDefaults.Web) + JsonStringEnumConverter` exactly.
- **Use hyphens, not em/en dashes**, in all files.
- Commit messages follow repo convention (end with the `Co-Authored-By` / `Claude-Session` trailer).
- Work happens on branch `cloudflare-wasm-deploy`.

---

## File Structure

**New files**
- `src/FactorioTools.Contract/FactorioTools.Contract.csproj` - the shared project.
- `src/FactorioTools.Contract/OilFieldPlanRequestResponse.cs`, `OilFieldPlanRequest.cs`, `OilFieldPlanResponse.cs`, `OilFieldNormalizeRequestResponse.cs`, `OilFieldNormalizeRequest.cs`, `OilFieldNormalizeResponse.cs` - moved DTOs (namespace `Knapcode.FactorioTools.Contract`).
- `src/FactorioTools.Contract/ErrorEnvelope.cs` - POCO error shape.
- `src/FactorioTools.Contract/ContractJsonContext.cs` - source-gen `JsonSerializerContext` + `ContractJson.Options`.
- `src/FactorioTools.Contract/PlanOrchestrator.cs` - the two-surface orchestrator.
- `src/vue/src/lib/wasmPlanner.ts` - boots dotnet.js, exposes `plan`/`normalize`.
- `.github/workflows/deploy-cloudflare.yml` - the deploy workflow.
- `test/FactorioTools.Test/OilField/PlanOrchestratorTest.cs`, `ContractSerializationTest.cs` - new tests.

**Modified files**
- `src/WebApp/WebApp.csproj` (add Contract reference), `src/WebApp/Controllers/OilFieldController.cs`, `src/WebApp/Program.cs`, `src/WebApp/Models/OilFieldPlanRequestDefaultsSchemaFilter.cs`, `src/WebApp/Models/RequireNonNullablePropertiesSchemaFilter.cs` (usings only).
- `src/BrowserWasm/BrowserWasm.csproj`, `src/BrowserWasm/Program.cs`, `src/BrowserWasm/main.js`.
- `src/vue/src/lib/OilFieldPlanner.ts`, `src/vue/src/views/OilField.vue`, `src/vue/package.json`, `src/vue/.gitignore` (or repo `.gitignore`).
- `FactorioTools.sln`, `CLAUDE.md`.

---

## Task 1: Create FactorioTools.Contract and move the DTOs

**Files:**
- Create: `src/FactorioTools.Contract/FactorioTools.Contract.csproj`
- Move (git mv) 6 files from `src/WebApp/Models/` to `src/FactorioTools.Contract/`: `OilFieldPlanRequestResponse.cs`, `OilFieldPlanRequest.cs`, `OilFieldPlanResponse.cs`, `OilFieldNormalizeRequestResponse.cs`, `OilFieldNormalizeRequest.cs`, `OilFieldNormalizeResponse.cs`
- Modify: `FactorioTools.sln`, `src/WebApp/WebApp.csproj`, `src/WebApp/Controllers/OilFieldController.cs`, `src/WebApp/Program.cs`, `src/WebApp/Models/OilFieldPlanRequestDefaultsSchemaFilter.cs`, `src/WebApp/Models/RequireNonNullablePropertiesSchemaFilter.cs`

**Interfaces:**
- Produces: namespace `Knapcode.FactorioTools.Contract` containing `OilFieldPlanRequest : OilFieldPlanRequestResponse : OilFieldOptions`, `OilFieldPlanResponse(OilFieldPlanRequestResponse Request, string Blueprint, OilFieldPlanSummary Summary)`, `OilFieldNormalizeRequest : OilFieldNormalizeRequestResponse`, `OilFieldNormalizeResponse(OilFieldNormalizeRequestResponse Request, string Blueprint)`.

- [ ] **Step 1: Capture the current swagger.json as the parity baseline**

```bash
cp src/WebApp/swagger.json /tmp/swagger.before.json
```

- [ ] **Step 2: Create the Contract csproj**

Create `src/FactorioTools.Contract/FactorioTools.Contract.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);1591</NoWarn>
    <IsTrimmable>true</IsTrimmable>
  </PropertyGroup>

  <PropertyGroup>
    <RootNamespace>Knapcode.FactorioTools.Contract</RootNamespace>
    <AssemblyName>Knapcode.FactorioTools.Contract</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\FactorioTools\FactorioTools.csproj" />
    <ProjectReference Include="..\FactorioTools.Serialization\FactorioTools.Serialization.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Move the 6 DTO files and rewrite their namespace**

```bash
git mv src/WebApp/Models/OilFieldPlanRequestResponse.cs src/FactorioTools.Contract/
git mv src/WebApp/Models/OilFieldPlanRequest.cs src/FactorioTools.Contract/
git mv src/WebApp/Models/OilFieldPlanResponse.cs src/FactorioTools.Contract/
git mv src/WebApp/Models/OilFieldNormalizeRequestResponse.cs src/FactorioTools.Contract/
git mv src/WebApp/Models/OilFieldNormalizeRequest.cs src/FactorioTools.Contract/
git mv src/WebApp/Models/OilFieldNormalizeResponse.cs src/FactorioTools.Contract/
```

In each moved file, change `namespace Knapcode.FactorioTools.WebApp.Models;` to `namespace Knapcode.FactorioTools.Contract;`. Each file keeps its existing `using Knapcode.FactorioTools.OilField;` where present (the response records and `OilFieldPlanRequestResponse` need it for `OilFieldPlanSummary` / `OilFieldOptions`).

- [ ] **Step 4: Add the project to the solution**

```bash
# Append a project entry + config rows to FactorioTools.sln (mirror the FactorioTools.Serialization entry).
# Use a fresh GUID, e.g. generate with: uuidgen
```

Add a `Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FactorioTools.Contract", "src\FactorioTools.Contract\FactorioTools.Contract.csproj", "{<NEW-GUID>}"` line near the other `src\` projects and the matching `GlobalSection(ProjectConfigurationPlatforms)` Debug/Release rows.

- [ ] **Step 5: Reference Contract from WebApp and fix usings**

In `src/WebApp/WebApp.csproj`, add under the existing `<ItemGroup>` with project references:

```xml
    <ProjectReference Include="..\FactorioTools.Contract\FactorioTools.Contract.csproj" />
```

In `src/WebApp/Controllers/OilFieldController.cs`, replace `using Knapcode.FactorioTools.WebApp.Models;` with `using Knapcode.FactorioTools.Contract;`.

In `src/WebApp/Program.cs`, replace `using Knapcode.FactorioTools.WebApp.Models;` with `using Knapcode.FactorioTools.Contract;`. The schema-filter registrations stay (the filter classes remain in WebApp); they now resolve the DTO types from Contract.

In `src/WebApp/Models/OilFieldPlanRequestDefaultsSchemaFilter.cs` and `src/WebApp/Models/RequireNonNullablePropertiesSchemaFilter.cs`, these stay in namespace `Knapcode.FactorioTools.WebApp.Models` but add `using Knapcode.FactorioTools.Contract;` so `OilFieldPlanRequest` etc. resolve.

- [ ] **Step 6: Build WebApp**

Run: `./docker-build.sh build src/WebApp/WebApp.csproj -c Release`
Expected: `Build succeeded. 0 Error(s)` (ImageSharp NU190x warnings only appear under `EnableVisualizer`, not here).

- [ ] **Step 7: Regenerate swagger.json and diff against the baseline**

```bash
./docker-build.sh build src/WebApp/WebApp.csproj -c Debug
diff /tmp/swagger.before.json src/WebApp/swagger.json && echo "SWAGGER UNCHANGED"
```
Expected: `SWAGGER UNCHANGED` (the swagger.json is produced by the WebApp build's `dotnet swagger tofile` post-build step; schema names are unchanged because they derive from type names).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Extract DTOs into FactorioTools.Contract project"
```

---

## Task 2: Source-gen JSON context, error envelope, shared options

**Files:**
- Create: `src/FactorioTools.Contract/ErrorEnvelope.cs`
- Create: `src/FactorioTools.Contract/ContractJsonContext.cs`
- Create: `test/FactorioTools.Test/OilField/ContractSerializationTest.cs`
- Modify: `test/FactorioTools.Test/FactorioTools.Test.csproj` (add Contract reference)

**Interfaces:**
- Produces: `Knapcode.FactorioTools.Contract.ErrorEnvelope { string Title; int Status; Dictionary<string, List<string>> Errors; }`; `ContractJson.Options` (a `JsonSerializerOptions`); `ContractJsonContext : JsonSerializerContext`.
- Consumes: DTO types from Task 1; `PipeStrategy`, `BeaconStrategy` from `Knapcode.FactorioTools.OilField`.

- [ ] **Step 1: Create the error envelope POCO**

Create `src/FactorioTools.Contract/ErrorEnvelope.cs`:

```csharp
using System.Collections.Generic;

namespace Knapcode.FactorioTools.Contract;

/// <summary>
/// Plain error shape mirroring the JSON the WebApp ExceptionFilter produces
/// (title + status + an errors dictionary keyed by "FactorioToolsException").
/// Deliberately not Microsoft.AspNetCore.Mvc.ProblemDetails (unavailable in WASM).
/// </summary>
public class ErrorEnvelope
{
    public string Title { get; set; } = null!;
    public int Status { get; set; }
    public Dictionary<string, List<string>> Errors { get; set; } = new();
}
```

- [ ] **Step 2: Create the source-gen context + shared options**

Create `src/FactorioTools.Contract/ContractJsonContext.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Knapcode.FactorioTools.OilField;

namespace Knapcode.FactorioTools.Contract;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(OilFieldPlanRequest))]
[JsonSerializable(typeof(OilFieldPlanResponse))]
[JsonSerializable(typeof(OilFieldNormalizeRequest))]
[JsonSerializable(typeof(OilFieldNormalizeResponse))]
[JsonSerializable(typeof(ErrorEnvelope))]
internal partial class ContractJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Trim-safe serialization options used at the JS/WASM boundary. Matches WebApp's
/// AddJsonOptions (Web defaults + JsonStringEnumConverter) so payloads are identical.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = ContractJsonContext.Default,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new JsonStringEnumConverter<PipeStrategy>());
        options.Converters.Add(new JsonStringEnumConverter<BeaconStrategy>());
        return options;
    }
}
```

- [ ] **Step 3: Reference Contract from the test project**

In `test/FactorioTools.Test/FactorioTools.Test.csproj`, add next to the existing project reference:

```xml
    <ProjectReference Include="..\..\src\FactorioTools.Contract\FactorioTools.Contract.csproj" />
```

- [ ] **Step 4: Write the serialization parity test (failing)**

Create `test/FactorioTools.Test/OilField/ContractSerializationTest.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Knapcode.FactorioTools.Contract;
using Xunit;

namespace Knapcode.FactorioTools.OilField;

public class ContractSerializationTest
{
    // Replicates WebApp Program.cs: AddJsonOptions => Web defaults + JsonStringEnumConverter.
    private static readonly JsonSerializerOptions WebAppOptions = CreateWebAppOptions();

    private static JsonSerializerOptions CreateWebAppOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [Fact]
    public void PlanRequest_SourceGen_MatchesWebAppReflection()
    {
        var request = new OilFieldPlanRequest { Blueprint = "BP" };

        var reflection = JsonSerializer.Serialize(request, WebAppOptions);
        var sourceGen = JsonSerializer.Serialize(request, ContractJson.Options);

        Assert.Equal(reflection, sourceGen);
    }

    [Fact]
    public void PlanRequest_RoundTrips_WithStringEnums()
    {
        var json = JsonSerializer.Serialize(
            new OilFieldPlanRequest { Blueprint = "BP" }, ContractJson.Options);

        Assert.Contains("\"ConnectedCentersDelaunay\"", json); // enum as PascalCase string
        Assert.Contains("\"blueprint\":\"BP\"", json);          // property camelCase

        var back = JsonSerializer.Deserialize<OilFieldPlanRequest>(json, ContractJson.Options)!;
        Assert.Equal("BP", back.Blueprint);
    }
}
```

- [ ] **Step 5: Run the tests, expect FAIL initially if config is off**

Run: `./docker-build.sh test test/FactorioTools.Test/FactorioTools.Test.csproj -c Release --filter ContractSerializationTest`
Expected: PASS. If `PlanRequest_SourceGen_MatchesWebAppReflection` fails, the diff in the assertion message shows the exact mismatch (property casing or enum casing) - adjust `ContractJson.Options` until it matches, then re-run. (This test is the parity gate per the spec.)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Add source-gen JSON context, error envelope, and parity test"
```

---

## Task 3: PlanOrchestrator typed (throwing) surface

**Files:**
- Create: `src/FactorioTools.Contract/PlanOrchestrator.cs`
- Create: `test/FactorioTools.Test/OilField/PlanOrchestratorTest.cs`

**Interfaces:**
- Produces: `PlanOrchestrator.Plan(OilFieldPlanRequest) -> OilFieldPlanResponse` and `PlanOrchestrator.Normalize(OilFieldNormalizeRequest) -> OilFieldNormalizeResponse`, both throwing `FactorioToolsException` on bad input.
- Consumes: `ParseBlueprint.Execute`, `GridToBlueprintString.Execute` / `.SerializeBlueprint` (Serialization), `Planner.Execute`, `CleanBlueprint.Execute` (core).

- [ ] **Step 1: Write the typed orchestrator**

Create `src/FactorioTools.Contract/PlanOrchestrator.cs` (mirrors the current controller bodies exactly):

```csharp
using Knapcode.FactorioTools.OilField;

namespace Knapcode.FactorioTools.Contract;

public static class PlanOrchestrator
{
    public static OilFieldPlanResponse Plan(OilFieldPlanRequest request)
    {
        var parsedBlueprint = ParseBlueprint.Execute(request.Blueprint);
        var result = Planner.Execute(request, parsedBlueprint);
        var outputBlueprint = GridToBlueprintString.Execute(result.Context, request.AddFbeOffset, addAvoidEntities: false);
        return new OilFieldPlanResponse(request, outputBlueprint, result.Summary);
    }

    public static OilFieldNormalizeResponse Normalize(OilFieldNormalizeRequest request)
    {
        var parsedBlueprint = ParseBlueprint.Execute(request.Blueprint);
        var clean = CleanBlueprint.Execute(parsedBlueprint);
        var outputBlueprint = GridToBlueprintString.SerializeBlueprint(clean, addFbeOffset: false);
        return new OilFieldNormalizeResponse(request, outputBlueprint);
    }
}
```

- [ ] **Step 2: Write tests (failing until Step 1 compiles)**

Create `test/FactorioTools.Test/OilField/PlanOrchestratorTest.cs`:

```csharp
using Knapcode.FactorioTools.Contract;
using Xunit;

namespace Knapcode.FactorioTools.OilField;

public class PlanOrchestratorTest
{
    // A small valid single-pumpjack blueprint (from the swagger default example).
    private const string SampleBlueprint = "0eJyMj70OwjAMhN/lZg8NbHkVhFB/rMrQuFGSIqoq707aMiCVgcWSz+fP5wXNMLEPogl2gbSjRtjLgii91sOqae0YFn5y/l63DxDS7FdFEjtkgmjHL1iTrwTWJEl4Z2zNfNPJNRyKgX6w/BjLwqjrpQI5E+ZSC7WTwO0+qTIdYKc/YKbaaOaAK0G38Pbre8KTQ/wY8hsAAP//AwAEfF3F";

    [Fact]
    public void Plan_ReturnsNonEmptyBlueprint()
    {
        var response = PlanOrchestrator.Plan(new OilFieldPlanRequest { Blueprint = SampleBlueprint });
        Assert.False(string.IsNullOrEmpty(response.Blueprint));
        Assert.NotNull(response.Summary);
    }

    [Fact]
    public void Normalize_ReturnsNonEmptyBlueprint()
    {
        var response = PlanOrchestrator.Normalize(new OilFieldNormalizeRequest { Blueprint = SampleBlueprint });
        Assert.False(string.IsNullOrEmpty(response.Blueprint));
    }

    [Fact]
    public void Plan_BadInput_ThrowsFactorioToolsException()
    {
        var ex = Assert.Throws<FactorioToolsException>(
            () => PlanOrchestrator.Plan(new OilFieldPlanRequest { Blueprint = "not-a-blueprint" }));
        Assert.True(ex.BadInput || !ex.BadInput); // exception type is the contract; flag asserted in Task 4
    }
}
```

- [ ] **Step 3: Run the tests**

Run: `./docker-build.sh test test/FactorioTools.Test/FactorioTools.Test.csproj -c Release --filter PlanOrchestratorTest`
Expected: PASS (3 tests).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Add typed PlanOrchestrator surface with tests"
```

---

## Task 4: PlanOrchestrator JSON (catching) surface

**Files:**
- Modify: `src/FactorioTools.Contract/PlanOrchestrator.cs`
- Modify: `test/FactorioTools.Test/OilField/PlanOrchestratorTest.cs`

**Interfaces:**
- Produces: `PlanOrchestrator.PlanJson(string) -> string` and `PlanOrchestrator.NormalizeJson(string) -> string`. On `FactorioToolsException` they return `ErrorEnvelope` JSON (`status` 400 when `BadInput`, else 500); otherwise the success response JSON.

- [ ] **Step 1: Add the JSON wrappers**

Add to `src/FactorioTools.Contract/PlanOrchestrator.cs` (add `using System.Text.Json;` at top):

```csharp
    public static string PlanJson(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<OilFieldPlanRequest>(requestJson, ContractJson.Options)!;
            var response = Plan(request);
            return JsonSerializer.Serialize(response, ContractJson.Options);
        }
        catch (FactorioToolsException ex)
        {
            return JsonSerializer.Serialize(ToEnvelope(ex), ContractJson.Options);
        }
    }

    public static string NormalizeJson(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<OilFieldNormalizeRequest>(requestJson, ContractJson.Options)!;
            var response = Normalize(request);
            return JsonSerializer.Serialize(response, ContractJson.Options);
        }
        catch (FactorioToolsException ex)
        {
            return JsonSerializer.Serialize(ToEnvelope(ex), ContractJson.Options);
        }
    }

    private static ErrorEnvelope ToEnvelope(FactorioToolsException ex)
    {
        var messages = new List<string>();
        Exception? current = ex;
        while (current != null)
        {
            messages.Add(current.Message);
            current = current.InnerException;
        }

        return new ErrorEnvelope
        {
            Title = ex.BadInput ? "Bad input was provided." : "A FactorioTools exception occurred.",
            Status = ex.BadInput ? 400 : 500,
            Errors = new Dictionary<string, List<string>> { [nameof(FactorioToolsException)] = messages },
        };
    }
```

Add `using System.Collections.Generic;` if not already present.

- [ ] **Step 2: Add JSON-surface tests**

Append to `test/FactorioTools.Test/OilField/PlanOrchestratorTest.cs` (add `using System.Text.Json;` and `using Knapcode.FactorioTools.Contract;`):

```csharp
    [Fact]
    public void PlanJson_HappyPath_ReturnsResponseJson()
    {
        var requestJson = JsonSerializer.Serialize(
            new OilFieldPlanRequest { Blueprint = SampleBlueprint }, ContractJson.Options);

        var responseJson = PlanOrchestrator.PlanJson(requestJson);

        var response = JsonSerializer.Deserialize<OilFieldPlanResponse>(responseJson, ContractJson.Options)!;
        Assert.False(string.IsNullOrEmpty(response.Blueprint));
    }

    [Fact]
    public void PlanJson_BadInput_ReturnsErrorEnvelope()
    {
        var requestJson = JsonSerializer.Serialize(
            new OilFieldPlanRequest { Blueprint = "not-a-blueprint" }, ContractJson.Options);

        var responseJson = PlanOrchestrator.PlanJson(requestJson);

        var envelope = JsonSerializer.Deserialize<ErrorEnvelope>(responseJson, ContractJson.Options)!;
        Assert.Equal(400, envelope.Status);
        Assert.True(envelope.Errors.ContainsKey("FactorioToolsException"));
        Assert.NotEmpty(envelope.Errors["FactorioToolsException"]);
    }
```

- [ ] **Step 3: Run the tests**

Run: `./docker-build.sh test test/FactorioTools.Test/FactorioTools.Test.csproj -c Release --filter PlanOrchestratorTest`
Expected: PASS (5 tests).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Add JSON PlanOrchestrator surface with error envelope"
```

---

## Task 5: Delegate WebApp controller to the orchestrator

**Files:**
- Modify: `src/WebApp/Controllers/OilFieldController.cs`

**Interfaces:**
- Consumes: `PlanOrchestrator.Plan` / `.Normalize` (Task 3).

- [ ] **Step 1: Collapse the controller actions to delegations**

Replace the bodies of `NormalizeBlueprint` and `GetPlan` in `src/WebApp/Controllers/OilFieldController.cs` so they delegate (keep the `[HttpPost]`/`[EnableCors]` attributes, the logging, and the typed signatures; exceptions still propagate to `ExceptionFilter`):

```csharp
    [HttpPost("normalize")]
    [EnableCors]
    public OilFieldNormalizeResponse NormalizeBlueprint([FromBody] OilFieldNormalizeRequest request)
    {
        _logger.LogInformation("Normalizing blueprint {Blueprint}", request.Blueprint);
        return PlanOrchestrator.Normalize(request);
    }

    [HttpPost("plan")]
    [EnableCors]
    public OilFieldPlanResponse GetPlan([FromBody] OilFieldPlanRequest request)
    {
        _logger.LogInformation("Planning oil field for blueprint {Blueprint}", request.Blueprint);
        return PlanOrchestrator.Plan(request);
    }
```

- [ ] **Step 2: Build WebApp and re-verify swagger is unchanged**

```bash
./docker-build.sh build src/WebApp/WebApp.csproj -c Debug
diff /tmp/swagger.before.json src/WebApp/swagger.json && echo "SWAGGER UNCHANGED"
```
Expected: `Build succeeded` and `SWAGGER UNCHANGED`.

- [ ] **Step 3: Run the full test suite (no regressions)**

Run: `./docker-build.sh test test/FactorioTools.Test/FactorioTools.Test.csproj -c Release`
Expected: all tests pass (the 4072 core tests plus the new orchestrator/serialization tests).

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Delegate WebApp controller to PlanOrchestrator"
```

---

## Task 6: BrowserWasm exports

**Files:**
- Modify: `src/BrowserWasm/BrowserWasm.csproj`, `src/BrowserWasm/Program.cs`, `src/BrowserWasm/main.js`

**Interfaces:**
- Produces: JS-visible `exports.Interop.Plan(requestJson)` and `exports.Interop.Normalize(requestJson)` (the assembly exports class is named `Interop`).
- Consumes: `PlanOrchestrator.PlanJson` / `.NormalizeJson` (Task 4).

- [ ] **Step 1: Turn AOT off and reference Contract**

In `src/BrowserWasm/BrowserWasm.csproj`, change `<RunAOTCompilation>true</RunAOTCompilation>` to `<RunAOTCompilation>false</RunAOTCompilation>` (leave `PublishTrimmed`/`TrimMode=full` as-is), and add to the existing `<ItemGroup>` with the project reference:

```xml
    <ProjectReference Include="..\FactorioTools.Contract\FactorioTools.Contract.csproj" />
```

- [ ] **Step 2: Replace the demo exports**

Replace the body of `src/BrowserWasm/Program.cs` with:

```csharp
using System.Runtime.InteropServices.JavaScript;
using Knapcode.FactorioTools.Contract;

public class Program
{
    private static void Main(string[] args)
    {
    }
}

public partial class Interop
{
    [JSExport]
    public static string Plan(string requestJson) => PlanOrchestrator.PlanJson(requestJson);

    [JSExport]
    public static string Normalize(string requestJson) => PlanOrchestrator.NormalizeJson(requestJson);
}
```

- [ ] **Step 3: Update the standalone main.js so it stops calling Greeting**

In `src/BrowserWasm/main.js`, replace `console.log(exports.MyClass.Greeting())` with:

```javascript
console.log('FactorioTools WASM ready', Object.keys(exports.Interop))
```

- [ ] **Step 4: Publish the WASM (Release, AOT off) and confirm it builds + size**

Run:
```bash
./docker-build.sh bash -c "dotnet workload restore && dotnet publish src/BrowserWasm/BrowserWasm.csproj -c Release"
```
Expected: `Build succeeded`. Then check the largest payloads are within Cloudflare Pages' ~25 MiB per-file limit:
```bash
find src/BrowserWasm/bin/Release/net8.0/browser-wasm/publish/wwwroot/_framework -name "*.wasm" -exec ls -lh {} \; | sort -k5 -h | tail -5
```
Expected: each `.wasm` well under 25 MiB.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Add Plan/Normalize JSExports to BrowserWasm"
```

---

## Task 7: build-wasm script, asset ignore, CLAUDE.md note

**Files:**
- Modify: `src/vue/package.json`, `.gitignore`, `CLAUDE.md`

**Interfaces:**
- Produces: `npm run build-wasm` populating `src/vue/public/_framework` + `src/vue/public/dotnet.js` from a `BrowserWasm` publish.

- [ ] **Step 1: Add the build-wasm script**

In `src/vue/package.json` `"scripts"`, add (keep existing entries):

```json
    "build-wasm": "dotnet publish ../BrowserWasm/BrowserWasm.csproj -c Release && shx rm -rf public/_framework public/dotnet.js && shx cp -r ../BrowserWasm/bin/Release/net8.0/browser-wasm/publish/wwwroot/_framework ../BrowserWasm/bin/Release/net8.0/browser-wasm/publish/wwwroot/dotnet.js public/"
```

Note: `shx` is already a devDependency. The publish output path is confirmed from Task 6 Step 4 (`bin/Release/net8.0/browser-wasm/publish/wwwroot/`). Pin the exact path here against that output.

- [ ] **Step 2: Ignore the generated WASM assets**

Append to `.gitignore` under `# Custom`:

```
# Generated WASM assets copied into the Vue public dir for local dev / build
src/vue/public/_framework/
src/vue/public/dotnet.js
```

- [ ] **Step 3: Document the dev step in CLAUDE.md**

In `CLAUDE.md`, under the front-end / build section, add a bullet:

```
- The Vue app plans in-browser via .NET WASM. After changing C# planner code,
  run `npm run build-wasm` in `src/vue` to refresh `public/_framework` + `public/dotnet.js`
  (requires the .NET 8 SDK or run the publish via `./docker-build.sh`). `npm run dev` / `npm run build` serve those assets.
```

- [ ] **Step 4: Generate the assets locally and verify they land**

Run (host has only .NET 10; use docker for the publish, then copy):
```bash
./docker-build.sh bash -c "dotnet workload restore && dotnet publish src/BrowserWasm/BrowserWasm.csproj -c Release"
mkdir -p src/vue/public && rm -rf src/vue/public/_framework src/vue/public/dotnet.js
cp -r src/BrowserWasm/bin/Release/net8.0/browser-wasm/publish/wwwroot/_framework src/BrowserWasm/bin/Release/net8.0/browser-wasm/publish/wwwroot/dotnet.js src/vue/public/
ls src/vue/public/_framework/dotnet.js src/vue/public/dotnet.js 2>/dev/null; ls src/vue/public/_framework | head
```
Expected: `_framework` populated and `public/dotnet.js` present. (On a machine with the .NET 8 SDK, `npm run build-wasm` does this in one step.)

- [ ] **Step 5: Commit (script + ignore + doc only; not the generated assets)**

```bash
git add src/vue/package.json .gitignore CLAUDE.md
git commit -m "Add build-wasm dev script and document WASM asset sync"
```

---

## Task 8: Vue wasmPlanner module

**Files:**
- Create: `src/vue/src/lib/wasmPlanner.ts`

**Interfaces:**
- Produces: `export async function plan(requestJson: string): Promise<string>` and `export async function normalize(requestJson: string): Promise<string>` from `wasmPlanner.ts`.
- Consumes: published `dotnet.js` at the site root (`/dotnet.js`), exports class `Interop` (Task 6).

- [ ] **Step 1: Write the singleton-boot WASM loader**

Create `src/vue/src/lib/wasmPlanner.ts`:

```typescript
// Boots the .NET WASM runtime once and exposes the Plan/Normalize exports.
// dotnet.js is published into /public by `npm run build-wasm` and served at the site root.

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
      const dotnetUrl = `${__BASE_PATH__}dotnet.js`
      const { dotnet } = await import(/* @vite-ignore */ dotnetUrl)
      const { getAssemblyExports, getConfig } = await dotnet
        .withDiagnosticTracing(false)
        .create()
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
```

- [ ] **Step 2: Typecheck the front-end**

Run (in `src/vue`): `npx vue-tsc --noEmit`
Expected: no errors. (`__BASE_PATH__` is a Vite `define` global already declared in the project; if vue-tsc complains it is undeclared, add `declare const __BASE_PATH__: string` to an existing ambient `.d.ts`, e.g. `src/vue/src/clipboardy.d.ts`.)

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "Add Vue wasmPlanner module"
```

---

## Task 9: Rewire OilFieldPlanner.ts to WASM + yield-to-paint

**Files:**
- Modify: `src/vue/src/lib/OilFieldPlanner.ts`, `src/vue/src/views/OilField.vue`

**Interfaces:**
- Consumes: `wasmPlanner.plan` / `.normalize` (Task 8). Keeps the existing exported `getPlan()` / `normalize()` / `ApiResult` / `ApiError` contract used by the view and components.

- [ ] **Step 1: Replace the API client call path with WASM**

In `src/vue/src/lib/OilFieldPlanner.ts`, replace the `getApiResultOrError` helper (lines ~73-127) so it calls WASM and parses the JSON string. Keep `ApiError` / `ApiResult` shapes. New helper:

```typescript
import * as wasmPlanner from './wasmPlanner'

async function runWasm<Data>(
  requestJson: string,
  invoke: (json: string) => Promise<string>
): Promise<ApiResult<Data> | ApiError> {
  try {
    const responseJson = await invoke(requestJson)
    const parsed = JSON.parse(responseJson)

    // Error envelope shape: { title, status, errors }
    if (parsed && typeof parsed === 'object' && 'status' in parsed && 'errors' in parsed && !('blueprint' in parsed)) {
      const errors: Record<string, string[]> = {}
      if (parsed.errors && typeof parsed.errors === 'object') {
        for (const [key, values] of Object.entries(parsed.errors)) {
          if (Array.isArray(values)) {
            errors[key] = (values as unknown[]).filter((v): v is string => typeof v === 'string')
          }
        }
      }
      return { isError: true, title: parsed.title ?? 'An error occurred.', errors }
    }

    return { isError: false, data: parsed as Data }
  } catch (e) {
    return {
      isError: true,
      title: 'An unexpected error occurred.',
      errorDetails: [e instanceof Error ? (e.stack ?? e.toString()) : JSON.stringify(e)],
    }
  }
}
```

Update `normalize()` and `getPlan()` to build the request object, `JSON.stringify` it, and call `runWasm`:

```typescript
export async function normalize(): Promise<ApiResult<OilFieldNormalizeResponse> | ApiError> {
  const store = useOilFieldStore()
  const request: OilFieldNormalizeRequest = { blueprint: store.$state.inputBlueprint }
  return await runWasm<OilFieldNormalizeResponse>(JSON.stringify(request), wasmPlanner.normalize)
}

export async function getPlan(): Promise<ApiResult<OilFieldPlanResponse> | ApiError> {
  const store = useOilFieldStore()
  const request: OilFieldPlanRequest = { blueprint: "" }
  for (const [requestKey, getter] of getEntries(requestPropertyGetters)) {
    (request as any)[requestKey] = getter(store.$state)
  }
  return await runWasm<OilFieldPlanResponse>(JSON.stringify(request), wasmPlanner.plan)
}
```

Remove the now-unused `Api` / `HttpResponse` imports and the `baseUrl` / `useStagingApi` selection inside the old helper. (Leave the `useStagingApi` store field itself in place - out of scope to remove.)

- [ ] **Step 2: Yield to paint before the synchronous WASM call**

In `src/vue/src/views/OilField.vue`, in `invokeApi` (around line 286), add a yield after setting the loading flag so the spinner paints before the interpreter blocks the main thread:

```typescript
    async invokeApi<Data>(api: () => Promise<ApiResult<Data> | ApiError>) {
      if (this.cannotSubmit) {
        return
      }

      this.submitting = true;
      // A small non-zero delay reliably lets the browser paint the loading state
      // before the synchronous (IL-interpreted) WASM call blocks the main thread.
      await new Promise(r => setTimeout(r, 10))
      try {
        await api()
      } finally {
        this.submitting = false
      }
    },
```

- [ ] **Step 3: Typecheck + full Vue build**

Run (in `src/vue`, with the WASM assets present from Task 7 Step 4): `npm run build`
Expected: `swagger-gen` + `vue-tsc` + `vite build` all succeed; `dist/` contains `_framework` and `dotnet.js`.

- [ ] **Step 4: Manual browser verification**

Run (in `src/vue`): `npm run dev`, open the served URL, paste a sample blueprint, click Plan.
Expected: the loading state shows, then a planned blueprint renders. Paste an invalid blueprint and confirm the error view shows the "Bad input was provided." message. Compare the output for a known blueprint against the current Azure API output - they should match (same compiled core).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Plan in-browser via WASM instead of the Azure API"
```

---

## Task 10: Cloudflare deploy workflow + Pages provisioning

**Files:**
- Create: `.github/workflows/deploy-cloudflare.yml`

**Interfaces:**
- Consumes: the `npm run build` output (`src/vue/dist`) with WASM assets copied into `public/` during CI.

- [ ] **Step 1: Provision the Cloudflare Pages project and secrets**

Create the `factoriotools` Pages project (via the Cloudflare MCP/`wrangler`) and the deploy token. Then add GitHub repo secrets:
- `CLOUDFLARE_API_TOKEN` - a scoped token (Account > Cloudflare Pages: Edit). The maintainer creates this in the Cloudflare dashboard and pastes it into GitHub secrets.
- `CLOUDFLARE_ACCOUNT_ID` - the account id.

```bash
gh secret set CLOUDFLARE_API_TOKEN --repo wormeyman/FactorioTools   # paste token when prompted
gh secret set CLOUDFLARE_ACCOUNT_ID --repo wormeyman/FactorioTools  # paste account id
```

- [ ] **Step 2: Write the workflow**

Create `.github/workflows/deploy-cloudflare.yml`:

```yaml
name: Deploy to Cloudflare Pages

on:
  push:
    branches:
      - "main"
  workflow_dispatch:

permissions:
  contents: read

env:
  BUILD_PATH: "./src/vue"

jobs:
  deploy:
    name: Build and deploy
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          submodules: recursive
          fetch-depth: 0
      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json
      - name: Restore wasm-tools workload
        run: dotnet workload restore
      - name: Publish BrowserWasm
        run: dotnet publish src/BrowserWasm/BrowserWasm.csproj -c Release
      - name: Copy WASM assets into Vue public
        run: |
          rm -rf "$BUILD_PATH/public/_framework" "$BUILD_PATH/public/dotnet.js"
          cp -r src/BrowserWasm/bin/Release/net8.0/browser-wasm/publish/wwwroot/_framework \
                src/BrowserWasm/bin/Release/net8.0/browser-wasm/publish/wwwroot/dotnet.js \
                "$BUILD_PATH/public/"
      - name: Set up Node
        uses: actions/setup-node@v4
        with:
          node-version: 24
          cache: "npm"
          cache-dependency-path: ${{ env.BUILD_PATH }}/package-lock.json
      - name: Install dependencies
        run: npm install
        working-directory: ${{ env.BUILD_PATH }}
      - name: Build
        run: npm run build
        working-directory: ${{ env.BUILD_PATH }}
      - name: Deploy to Cloudflare Pages
        uses: cloudflare/wrangler-action@v3
        with:
          apiToken: ${{ secrets.CLOUDFLARE_API_TOKEN }}
          accountId: ${{ secrets.CLOUDFLARE_ACCOUNT_ID }}
          command: pages deploy ${{ env.BUILD_PATH }}/dist --project-name=factoriotools
```

Note: `BASE_PATH` is intentionally not set, so Vite `base` stays `/` for root serving.

- [ ] **Step 3: Validate the workflow YAML**

Run: `gh workflow view "Deploy to Cloudflare Pages" --repo wormeyman/FactorioTools` after pushing, or lint locally with `npx --yes @action-validator/cli .github/workflows/deploy-cloudflare.yml` if available.
Expected: no syntax errors.

- [ ] **Step 4: Commit and trigger**

```bash
git add .github/workflows/deploy-cloudflare.yml
git commit -m "Add Cloudflare Pages deploy workflow"
```

After this branch merges to `main` (or via `workflow_dispatch`), confirm the run succeeds and `https://factoriotools.pages.dev` plans a blueprint in-browser.

- [ ] **Step 5: Verify the deployed site**

Open `https://factoriotools.pages.dev`, paste a sample blueprint, and confirm planning works with no network calls to `*.azurewebsites.net` (check the browser Network tab - only same-origin `_framework` requests).

---

## Verification (end-to-end)

1. `./docker-build.sh test test/FactorioTools.Test/FactorioTools.Test.csproj -c Release` - all tests green (core + orchestrator + serialization parity).
2. `./docker-build.sh build src/FactorioTools/FactorioTools.csproj -c Release /p:UseLuaSettings=true` - Lua build still clean (Contract is not in the Lua build, core untouched).
3. `diff /tmp/swagger.before.json src/WebApp/swagger.json` - empty (API contract preserved).
4. `npm run build` in `src/vue` - green, `dist` has `_framework` + `dotnet.js`.
5. Browser: plan a known blueprint locally and on `factoriotools.pages.dev`; output matches the current Azure API for the same input; bad input shows the error envelope; Network tab shows no Azure calls.
6. WASM payload: largest `.wasm` < 25 MiB; total file count well under 20,000 (Cloudflare Pages limits).

## Self-Review notes

- **Spec coverage:** Contract project + DTO move (Task 1), source-gen + enum parity + error DTO (Task 2), two orchestrator surfaces (Tasks 3-4), WebApp delegation preserving ExceptionFilter (Task 5), BrowserWasm exports with AOT off / trim on (Task 6), local dev script (Task 7), wasmPlanner + front-end rewire + yield-to-paint (Tasks 8-9), Cloudflare workflow + provisioning (Task 10), testing + risks covered in Verification. All spec sections map to a task.
- **Interpreted-speed risk** (spec Risks): measured in Task 9 Step 4 / Task 10 Step 5; if too slow, re-enable AOT in `BrowserWasm.csproj` and re-check payload size.
- **Open follow-ups** (retire Azure/Vercel/GH Pages workflows; optional API fallback) remain out of scope per the spec.
