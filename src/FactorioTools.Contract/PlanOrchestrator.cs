using System;
using System.Collections.Generic;
using System.Text.Json;
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

    public static string PlanJson(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize(requestJson, ContractJson.Context.OilFieldPlanRequest)!;
            var response = Plan(request);
            return JsonSerializer.Serialize(response, ContractJson.Context.OilFieldPlanResponse);
        }
        catch (FactorioToolsException ex)
        {
            return JsonSerializer.Serialize(ToEnvelope(ex), ContractJson.Context.ErrorEnvelope);
        }
    }

    public static string NormalizeJson(string requestJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize(requestJson, ContractJson.Context.OilFieldNormalizeRequest)!;
            var response = Normalize(request);
            return JsonSerializer.Serialize(response, ContractJson.Context.OilFieldNormalizeResponse);
        }
        catch (FactorioToolsException ex)
        {
            return JsonSerializer.Serialize(ToEnvelope(ex), ContractJson.Context.ErrorEnvelope);
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
}
