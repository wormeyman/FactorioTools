using Knapcode.FactorioTools.OilField;
using Knapcode.FactorioTools.Contract;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Knapcode.FactorioTools.WebApp.Controllers;

[ApiController]
[Route("api/v1/oil-field")]
public class OilFieldController : ControllerBase
{
    private readonly ILogger<OilFieldController> _logger;

    public OilFieldController(ILogger<OilFieldController> logger)
    {
        _logger = logger;
    }

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
}
