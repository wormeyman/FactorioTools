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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        options.Converters.Add(new JsonStringEnumConverter<PipeStrategy>());
        options.Converters.Add(new JsonStringEnumConverter<BeaconStrategy>());
        return options;
    }
}
