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
