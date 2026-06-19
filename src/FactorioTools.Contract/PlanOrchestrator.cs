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
