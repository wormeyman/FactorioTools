namespace Knapcode.FactorioTools.OilField;

public class MakeStraightLineTest
{
    [Fact]
    public void Horizontal_LeftToRight_AscendingX()
    {
        var line = Helpers.MakeStraightLine(new Location(0, 0), new Location(3, 0));

        Assert.Equal(
            new[]
            {
                new Location(0, 0),
                new Location(1, 0),
                new Location(2, 0),
                new Location(3, 0),
            },
            line);
    }

    [Fact]
    public void Vertical_TopToBottom_AscendingY()
    {
        var line = Helpers.MakeStraightLine(new Location(0, 0), new Location(0, 3));

        Assert.Equal(
            new[]
            {
                new Location(0, 0),
                new Location(0, 1),
                new Location(0, 2),
                new Location(0, 3),
            },
            line);
    }

    [Fact]
    public void ReversedEndpoints_StillNormalizeToAscendingOrder()
    {
        // The endpoints are passed high-to-low, but the output is always min..max.
        var line = Helpers.MakeStraightLine(new Location(0, 3), new Location(0, 0));

        Assert.Equal(
            new[]
            {
                new Location(0, 0),
                new Location(0, 1),
                new Location(0, 2),
                new Location(0, 3),
            },
            line);
    }

    [Fact]
    public void SamePoint_ReturnsSinglePoint()
    {
        var line = Helpers.MakeStraightLine(new Location(2, 2), new Location(2, 2));

        Assert.Equal(new[] { new Location(2, 2) }, line);
    }

    [Fact]
    public void Diagonal_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Helpers.MakeStraightLine(new Location(0, 0), new Location(1, 1)));
    }
}

public class CountTurnsTest
{
    [Fact]
    public void StraightLine_HasNoTurns()
    {
        var turns = Helpers.CountTurns(new List<Location>
        {
            new Location(0, 0),
            new Location(1, 0),
            new Location(2, 0),
        });

        Assert.Equal(0, turns);
    }

    [Fact]
    public void SingleCorner_HasOneTurn()
    {
        var turns = Helpers.CountTurns(new List<Location>
        {
            new Location(0, 0),
            new Location(1, 0),
            new Location(1, 1),
        });

        Assert.Equal(1, turns);
    }

    [Fact]
    public void Staircase_CountsEachDirectionChange()
    {
        var turns = Helpers.CountTurns(new List<Location>
        {
            new Location(0, 0),
            new Location(1, 0),
            new Location(1, 1),
            new Location(2, 1),
        });

        Assert.Equal(2, turns);
    }

    [Fact]
    public void SinglePoint_HasNoTurns()
    {
        var turns = Helpers.CountTurns(new List<Location> { new Location(0, 0) });

        Assert.Equal(0, turns);
    }
}

public class AreLocationsCollinearTest
{
    [Fact]
    public void DiagonalPoints_AreCollinear()
    {
        Assert.True(Helpers.AreLocationsCollinear(new List<Location>
        {
            new Location(0, 0),
            new Location(1, 1),
            new Location(2, 2),
        }));
    }

    [Fact]
    public void HorizontalPoints_AreCollinear()
    {
        Assert.True(Helpers.AreLocationsCollinear(new List<Location>
        {
            new Location(0, 0),
            new Location(1, 0),
            new Location(2, 0),
        }));
    }

    [Fact]
    public void VerticalPoints_AreCollinear()
    {
        Assert.True(Helpers.AreLocationsCollinear(new List<Location>
        {
            new Location(0, 0),
            new Location(0, 1),
            new Location(0, 2),
        }));
    }

    [Fact]
    public void BentPath_IsNotCollinear()
    {
        Assert.False(Helpers.AreLocationsCollinear(new List<Location>
        {
            new Location(0, 0),
            new Location(1, 0),
            new Location(1, 1),
        }));
    }

    [Fact]
    public void SinglePoint_IsCollinear()
    {
        Assert.True(Helpers.AreLocationsCollinear(new List<Location> { new Location(5, 5) }));
    }
}
