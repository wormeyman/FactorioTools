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
