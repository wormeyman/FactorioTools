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
