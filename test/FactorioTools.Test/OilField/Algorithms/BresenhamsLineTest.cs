namespace Knapcode.FactorioTools.OilField;

public class BresenhamsLineTest
{
    [Fact]
    public void SinglePoint_ReturnsThatPoint()
    {
        var path = BresenhamsLine.GetPath(new Location(5, 5), new Location(5, 5));

        Assert.Equal(new[] { new Location(5, 5) }, path);
    }

    [Fact]
    public void HorizontalLine_IncludesEveryPointLeftToRight()
    {
        var path = BresenhamsLine.GetPath(new Location(0, 0), new Location(3, 0));

        Assert.Equal(
            new[]
            {
                new Location(0, 0),
                new Location(1, 0),
                new Location(2, 0),
                new Location(3, 0),
            },
            path);
    }

    [Fact]
    public void VerticalLine_IncludesEveryPointTopToBottom()
    {
        var path = BresenhamsLine.GetPath(new Location(0, 0), new Location(0, 3));

        Assert.Equal(
            new[]
            {
                new Location(0, 0),
                new Location(0, 1),
                new Location(0, 2),
                new Location(0, 3),
            },
            path);
    }

    [Fact]
    public void PerfectDiagonal_StepsOneInEachAxisPerPoint()
    {
        var path = BresenhamsLine.GetPath(new Location(0, 0), new Location(2, 2));

        Assert.Equal(
            new[]
            {
                new Location(0, 0),
                new Location(1, 1),
                new Location(2, 2),
            },
            path);
    }

    [Fact]
    public void ShallowSlope_StepsAlongTheMajorAxis()
    {
        var path = BresenhamsLine.GetPath(new Location(0, 0), new Location(4, 2));

        Assert.Equal(
            new[]
            {
                new Location(0, 0),
                new Location(1, 1),
                new Location(2, 1),
                new Location(3, 2),
                new Location(4, 2),
            },
            path);
    }

    [Fact]
    public void ReversedEndpoints_ProducesTheReversedPath()
    {
        var forward = BresenhamsLine.GetPath(new Location(0, 0), new Location(3, 0));
        var backward = BresenhamsLine.GetPath(new Location(3, 0), new Location(0, 0));

        Assert.Equal(forward.AsEnumerable().Reverse(), backward);
    }
}
