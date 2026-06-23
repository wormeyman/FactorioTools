using Knapcode.FactorioTools.Data;

namespace Knapcode.FactorioTools.OilField;

public class PlannerTest : BasePlannerTest
{
    public static IReadOnlyList<string> BlueprintsWithIsolatedAreas = new[]
    {
        "0eJyU1ctugzAQheF3mbUXeGwg8CpVVRFiVW6Dg7hURYh3L2BX6sUSh2WI82Xi8OOZrvfRtJ11A5Uz2frheiqfZurtq6vu2zVXNYZKasemfavqdxI0TO12xQ6moUWQdTfzSaVcngUZN9jBGm/sL6YXNzZX060LRMRqH/36gYfbvmlFWAua1qVqdW+2M7V/L1nEP44BTjHMKYTLYU4jXOK57DfHES49MR3AZQAnA5cf/9gc4HTmucsxd0FulDBdccwVyN4pmJMJsnne4wTwTnTBEvCgMBLcO1EGM+AhaXCYDyhNIm2E24U14CFxfO+fPm5NInUojc+H5KHj80U9pA+Nz8dIHxz6AB4ujPShiqgX+z8Y6YPT3VNAHwz1wbgHHR3+aarSv956Bu/ncvnjYBf0Ybo+LFi+AAAA//8DAEf3mj4=",
        "0eJyM1ctuhCAUBuB3OWsWcvD+Kk0zcRzS0I6M8dLUGN+9KCzaGRL/pYifh8sPK13vs+4HYyeqVzLtw45Uv600mg/b3Pc223Saaurnrv9s2i8SNC393mIm3dEmyNib/qFabu+CtJ3MZLQ3joflYufuqgfXQUSs/jG6Dx52/5NDFAtaXFfl3JsZdOvfJZt44RjgZO65/JxTAMeF58pzLkW41HPVOZchg60OjpNzLkeWwlfH8pwrkMH6lWX+z6URrkS4LMrFqqsQroI5mSBrUXrvaR9zzENyIcNwM6A+JBhh6z170fqgZCTeA4ImoWgUuIdkQ0nvFYCHhGOflN0rgfmD0hE84CiQUDxK3EPyEfafAg4DRvIRxquA04CRfHAe9WLrwVA+vJe+nAfujjvuvfrPxSnoWw9j6LD9AgAA//8DAGH8ZjU=",
        "0eJyUl11vgjAUQP9Ln3mgvS0If2VZFj+ahW0iEVxmDP99aGsyJwmnjyIe29577r29qM3XyXfHph1UfVHN9tD2qn65qL55b9df12fteu9VrbrTvvtYbz9VpoZzd33SDH6vxkw17c7/qFqPr5ny7dAMjQ+M24fzW3vab/xxeiGbYXWHfvrBob3+0wSRIlPn6VWZuLvm6Lfhu3zMnnCG40y+jBOC0wGnl3EW4EzEmWWcIzgbcODsCoIrA84u48qEUADcKuHs3CPOzuCqhMi65dXpHPB03G0BeMSLezDKR56Z4xExjATeCqyPmGGqG0+AaJqoIcJ5yI0QX9Hg/JAcLvCAa5rYYUzgkXwhesR8RjzkR8g/KUFdJn7E/JMK8BL6hs2X42sS/LCkDyE/Qr5Ysl/kh+Y84keMrwO+GeJHrH8OdEqD/NCcR/ywkUfiS/yIvdeB/iHIjxXnIT8S1kf80HF9oL5IwmTlQH2RhNHKgf4mqH+Us7y5+iLED6n4+pAfMb6gHgjxQ4d6VZDBGfWPfJY3d36W+BHzmazPJsxXxb/6MjdO2oT+UYD6YokfOvLA9GyRH6E+F8BfS/y45wvw16L5Ku73yY/pjnm7d9Z/Lq6Z+vbHPr4w/gIAAP//AwAiyNgo",
    };

    public static IEnumerable<object[]> BlueprintsWithIsolatedAreasIndexes = Enumerable
        .Range(0, BlueprintsWithIsolatedAreas.Count)
        .Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(BlueprintsWithIsolatedAreasIndexes))]
    public void RejectsBlueprintWithBlockingIsolatedArea(int index)
    {
        var options = OilFieldOptions.ForMediumElectricPole;

        // this has a pumpjack that has it's top and right terminal blocked by other pumpjacks and the bottom and
        // left terminals pointed into an isolated area. There is probably a solution if you place underground pipes
        // from the beginning, but that's not supported today. Underground pipes are only optimized from a fully
        // connected system of above ground pipes.
        var blueprintString = BlueprintsWithIsolatedAreas[index];

        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        var ex = Assert.Throws<NoPathBetweenTerminalsException>(() => Planner.Execute(options, blueprint));
    }

    [Fact]
    public void AllowsPumpjackWithDefaultDirection()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        var blueprintString = "0eNqV1OtqgzAUAOB3Ob9DMTHHJL7KKMN2YWSrqXgZE8m7Tx1mgxp6/KmYz3PNBJfbYJvW+R7KCdz17jsoXybo3Luvbsu7fmwslOB6WwMDX9XLUzPUzUd1/YTAwPk3+w0lD2cG1veud/bXWB/GVz/UF9vOHzyeZtDcu/nA3S9/mhGp+QkZjFCKPDthCOyBERTG5M+YnMSojRFmn5EEBjOM0Yh9BknR/DHFPlOQShwZmYhGkZLKIpPvM5qUlIhMIilzrMQphmckJ3YcE4PD+bFeIU84lEFGHvcBE1XmlElGEbuFKuFIUl5FdBIbwQ/OciETDmmYjd4cpROOOlZnlaqPJtUZn8ZjSEtqNkcn+i5o8xzXS6/xzHf0epOX/y5+Bl+27dZDQnOpzBy7yDmiCOEHgSfwqQ==";
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        var (_, result) = Planner.Execute(options, blueprint);

        // Assert
        Assert.Equal(16, result.RotatedPumpjacks);
    }

    [Fact]
    public void AllowsElectricPolesToNotBePlanned()
    {
        // Arrange
        var options = OilFieldOptions.ForBigElectricPole;
        options.ValidateSolution = true;
        options.AddElectricPoles = false;
        var blueprintString = SmallListBlueprintStrings[0];
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        var (context, _) = Planner.Execute(options, blueprint);

        // Assert
        Assert.Empty(context.Grid.GetEntities().OfType<ElectricPoleCenter>());
        Assert.Empty(context.Grid.GetEntities().OfType<ElectricPoleSide>());
    }

    [Fact]
    public void AllowsBeaconsToNotBePlanned()
    {
        // Arrange
        var options = OilFieldOptions.ForBigElectricPole;
        options.ValidateSolution = true;
        options.AddBeacons = false;
        var blueprintString = SmallListBlueprintStrings[0];
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        var (context, _) = Planner.Execute(options, blueprint);

        // Assert
        Assert.Empty(context.Grid.GetEntities().OfType<BeaconCenter>());
        Assert.Empty(context.Grid.GetEntities().OfType<BeaconSide>());
    }

    [Fact]
    public async Task AllowsLocationsToBeAvoided()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        options.AddBeacons = true;
        var blueprint = new Blueprint
        {
            Entities = new[]
            {
                new Entity { Name = EntityNames.Vanilla.Pumpjack, Position = new Position { X = -3, Y = -5 } },
                new Entity { Name = EntityNames.Vanilla.Pumpjack, Position = new Position { X = 4, Y = 5 } },
            },
            Icons = new[]
            {
                new Icon
                {
                    Index = 1,
                    Signal = new SignalID
                    {
                        Name = EntityNames.Vanilla.Pumpjack,
                        Type = SignalTypes.Vanilla.Item,
                    }
                }
            },
            Item = ItemNames.Vanilla.Blueprint,
            Version = 0,
        };
        var avoid = Enumerable.Range(-7, 16).Select(x => new AvoidLocation(x, 0)).ToArray();

        // Act
        var result = Planner.Execute(options, blueprint, avoid);

        // Assert
#if USE_VERIFY
        await Verify(GetGridString(result));
#else
        await Task.Yield();
#endif
    }

    [Fact]
    public async Task AddsHeatPipesForAquilo()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        options.AddHeatPipes = true;
        options.AddBeacons = false; // best heat coverage is with beacons off
        var blueprintString = SmallListBlueprintStrings[0];
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        var result = Planner.Execute(options, blueprint);

        // Assert
        Assert.Empty(result.Context.Grid.GetEntities().OfType<BeaconCenter>());
        Assert.NotNull(result.Context.HeatPipes);
        Assert.NotEmpty(result.Context.Grid.GetEntities().OfType<HeatPipe>());
#if USE_VERIFY
        await Verify(GetGridString(result));
#else
        await Task.Yield();
#endif
    }

    [Fact]
    public async Task AddsHeatPipesAndBeaconsTogetherForAquilo()
    {
        // Arrange: heat pipes are the hard constraint, beacons are best-effort. Both enabled at once
        // must still produce a valid, fully heated field (heat wins; beacons fill the leftover space).
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        options.AddHeatPipes = true;
        options.AddBeacons = true;
        var blueprint = ParseBlueprint.Execute(SmallListBlueprintStrings[0]);

        // Act: must not throw - heat coverage and connectivity are validated inside Execute.
        var result = Planner.Execute(options, blueprint);

        // Assert: the field is fully heated and at least some beacons coexisted with the heat network.
        Assert.NotNull(result.Context.HeatPipes);
        Assert.NotEmpty(result.Context.Grid.GetEntities().OfType<HeatPipe>());
        Assert.NotEmpty(result.Context.Grid.GetEntities().OfType<BeaconCenter>());
#if USE_VERIFY
        await Verify(GetGridString(result));
#else
        await Task.Yield();
#endif
    }

    public static IEnumerable<object[]> SmallListBlueprintIndexes = Enumerable
        .Range(0, SmallListBlueprintStrings.Count)
        .Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(SmallListBlueprintIndexes))]
    public void EnablingBeaconsNeverBreaksAchievableHeatCoverage(int index)
    {
        // Heat pipes are the hard constraint; beacons are best-effort. The coexistence guarantee is: turning beacons ON
        // must never break a field that heat-only could fully heat. (Whether a field is heatable at all is a separate,
        // pre-existing property of the heat router - many dense fields cannot be fully heated even with beacons off, so
        // those are out of scope here.) When both are on, the planner routes heat first and only selects pipe layouts it
        // can fully heat, so any field heatable beacons-off stays fully heated with beacons on.
        var blueprintString = SmallListBlueprintStrings[index];

        var heatOnly = OilFieldOptions.ForMediumElectricPole;
        heatOnly.ValidateSolution = true;
        heatOnly.AddHeatPipes = true;
        heatOnly.AddBeacons = false;
        try
        {
            Planner.Execute(heatOnly, ParseBlueprint.Execute(blueprintString));
        }
        catch (FactorioToolsException)
        {
            // This field cannot be fully heated even without beacons - a pre-existing heat-router limitation, not a
            // coexistence regression. Skip it.
            return;
        }

        var bothOn = OilFieldOptions.ForMediumElectricPole;
        bothOn.ValidateSolution = true;
        bothOn.AddHeatPipes = true;
        bothOn.AddBeacons = true;

        var result = Planner.Execute(bothOn, ParseBlueprint.Execute(blueprintString));

        Assert.NotNull(result.Context.HeatPipes);
        Assert.NotEmpty(result.Context.Grid.GetEntities().OfType<HeatPipe>());
    }

    [Fact]
    public void EmitsHeatPipesInTwoPointZeroBlueprint()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        options.AddHeatPipes = true;
        var blueprint = ParseBlueprint.Execute(SmallListBlueprintStrings[0]);
        var (context, _) = Planner.Execute(options, blueprint);

        // Act
        var blueprintString = GridToBlueprintString.Execute(context, addFbeOffset: false, addAvoidEntities: false);
        var parsed = ParseBlueprint.Execute(blueprintString);

        // Assert
        var (major, _, _, _) = GridToBlueprintString.ParseVersion(parsed.Version);
        Assert.Equal(2, major);
        Assert.Contains(parsed.Entities, e => e.Name == EntityNames.Vanilla.HeatPipe);
    }

    [Fact]
    public async Task ExecuteSample()
    {
        var result = Planner.ExecuteSample();

#if USE_VERIFY
        await Verify(GetGridString(result));
#else
        await Task.Yield();
#endif
    }

    [Fact]
    public void SetsPumpjackCenterDirection()
    {
        var (context, _) = Planner.ExecuteSample();

        var centers = context
            .Grid
            .EntityLocations
            .EnumerateItems()
            .Select(l => (Location: l, Entity: (context.Grid[l] as PumpjackCenter)!))
            .Where(l => l.Entity is not null)
            .OrderBy(x => x.Location.Y)
            .ThenBy(x => x.Location.X)
            .ToList();
        Assert.Equal(4, centers.Count);
        Assert.Equal(Direction.Down, centers[0].Entity.Direction);
        Assert.Equal(Direction.Left, centers[1].Entity.Direction);
        Assert.Equal(Direction.Up, centers[2].Entity.Direction);
        Assert.Equal(Direction.Right, centers[3].Entity.Direction);
    }

    [Fact]
    public void SetsDeltasFromOriginalPositions()
    {
        var (context, _) = Planner.ExecuteSample();

        Assert.Equal(16, context.DeltaX);
        Assert.Equal(13, context.DeltaY);
    }

    [Fact]
    public void CountsAllRotatedPumpjacks()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        var blueprintString = "0eJyNkMsOgjAQRf/lrisJBcR26W8YY3hMTBVKU4qRkP67BaIxsnE3jztn7syEshnIWKUd5ARVdbqHPE3o1VUXzVxzoyFIKEctGHTRzpkZWnMrqjs8g9I1PSFjf2Yg7ZRTtDKWZLzooS3JBsF2msF0fRjo9LwpQHZJlDGMIciiLLBrZala+3vPNkj+B/JNTH+BfDa8nCW/vsDwINuvgkOc5oLnKRciEcF+U5QUfoLjR+39C6d7aOc=";
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        (_, var summary) = Planner.Execute(options, blueprint);

        // Assert
        Assert.Equal(2, summary.RotatedPumpjacks);
    }

    [Fact]
    public void CountsSomeRotatedPumpjacks()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        var blueprintString = "0eJyNkE0OgjAQhe/y1pWECkG69BrGmAITU6WlocVISO9ugWiMbNzNz5vvzcyEqh3I9sp4iAmq7oyDOE1w6mpkO9f8aAkCypMGg5F6zuyg7U3WdwQGZRp6QqThzEDGK69oZSzJeDGDrqiPgu00g+1cHOjM7BQhu32SM4wxyJM8shvVU732eWAbJP8D+SZmv8BsXng5S3x9geFBvVsdD2lWlLzIeFnuy7h+KyuKP8Hxow7hBaWraOU=";
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        (_, var summary) = Planner.Execute(options, blueprint);

        // Assert
        Assert.Equal(1, summary.RotatedPumpjacks);
    }

    [Fact]
    public void CountsNoRotatedPumpjacks()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        var blueprintString = "0eJyNkMsOgjAQRf/lriuRV7Bd+hvGGB4TU6WlKcVISP/dAtEY2bibx51zZ2ZC1Q5krNQOYoKsO91DnCb08qrLdq650RAEpCMFBl2qOTODMreyvsMzSN3QEyL2ZwbSTjpJK2NJxoseVEU2CLbTDKbrw0CnZ6cA2aVRzjCGII/ywG6kpXrtJ55tkMkfyDcx+wXu54WXs8TXFxgeZPvV8RBnBU+KLOE85WH9tqwo/ATHj9r7F6STaOE=";
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        (_, var summary) = Planner.Execute(options, blueprint);

        // Assert
        Assert.Equal(0, summary.RotatedPumpjacks);
    }

    [Fact]
    public void YieldsAlternateSolutions()
    {
        // Arrange
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        var blueprintString = "0eJyU1M1ugzAMB/B38TkH8sGgeZWqmii1pmwlRBCmIcS7L9QcNhUp7pFgfhiSvxe43icMg/MR7AKu7f0I9rzA6D58c9/WfNMhWAhTFz6b9gsExDlsKy5iB6sA52/4A1auFwHoo4sOyXhczO9+6q44pAJxYIV+TA/0fntTQowRMKdSndybG7Cle8UqnjjF4WriTJ7TDK6UxNV5zjA4Rd2pIs+VDE7unMxzbwxOK+IYW1FxuquIq/JczeE0cYytOHE4Onda5TlZ8L+W5XFyoUu+xwoG/T7NCIbkJEPu/ZX/PXXkcaKh5aF32N8L2TBPhznNrMccs38GoYBvHMa9YP0FAAD//wMAiTWvrw==";
        var blueprint = ParseBlueprint.Execute(blueprintString);

        // Act
        var (_, summary) = Planner.Execute(options, blueprint);

        // Assert
        Assert.Single(summary.SelectedPlans);
        Assert.Single(summary.AlternatePlans);
        Assert.NotEmpty(summary.UnusedPlans);
    }

    [Fact]
    public void HeatRouterDoesNotStrandReachablePipesBehindEnclosedSeed()
    {
        // Small-list index 6 has a high-coverage empty tile fully enclosed by pumpjacks and pipes. The greedy heat
        // router used to seed there and, unable to grow out of the pocket, abandon the rest of the field (1 heat pipe,
        // 44 reachable pipes left to freeze). The router must instead seed somewhere it can grow and fully heat the
        // field. Beacons off = best heat coverage.
        var options = OilFieldOptions.ForMediumElectricPole;
        options.ValidateSolution = true;
        options.AddHeatPipes = true;
        options.AddBeacons = false;

        // Must not throw - HeatPipesCoverAllTargets is validated inside Execute.
        var result = Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[6]));

        Assert.NotNull(result.Context.HeatPipes);
        Assert.True(result.Context.HeatPipes!.Count > 1, "the heat network should be more than the seed tile");
    }

    [Fact]
    public void HeatOnlyPrefersHeatableLayoutAcrossSmallList()
    {
        // Heat pipes are the hard constraint. When heat is enabled the planner generates many candidate pipe layouts
        // (multiple strategies, optimized and not); it must route heat per layout and prefer one it can fully heat,
        // rather than picking the fewest-pipe layout and discovering too late that it freezes. This raises how many of
        // the 61 small-list blueprints can be fully heated beacons-off. (Some dense fields have no heatable layout at
        // all - a separate, deferred pipe-routing limitation - so this is a threshold, not all 61.)
        var heated = 0;
        for (var index = 0; index < SmallListBlueprintStrings.Count; index++)
        {
            var options = OilFieldOptions.ForMediumElectricPole;
            options.ValidateSolution = true;
            options.AddHeatPipes = true;
            options.AddBeacons = false;
            try
            {
                Planner.Execute(options, ParseBlueprint.Execute(SmallListBlueprintStrings[index]));
                heated++;
            }
            catch (FactorioToolsException)
            {
            }
        }

        Assert.True(heated >= 35, $"expected heat-only to fully heat at least 35 of {SmallListBlueprintStrings.Count}, got {heated}");
    }

}
