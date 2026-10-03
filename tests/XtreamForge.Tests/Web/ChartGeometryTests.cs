using XtreamForge.Web.Components.Shared;

namespace XtreamForge.Tests.Web;

public class ChartGeometryTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 5)]
    [InlineData(10, 10)]
    [InlineData(11, 20)]
    [InlineData(21, 25)]
    [InlineData(240, 250)]
    [InlineData(4100, 5000)]
    [InlineData(50000, 50000)]
    public void NiceMaximum_RoundsUpToARoundValue(double value, double expected)
    {
        Assert.Equal(expected, ChartGeometry.NiceMaximum(value));
    }

    [Fact]
    public void ToPoints_SpreadsThePointsOverTheWidthWithZeroAtTheBottom()
    {
        var points = ChartGeometry.ToPoints([0, 5, 10], 10, 600, 160);

        Assert.Equal("0,160 300,80 600,0", points);
    }

    [Fact]
    public void ToPoints_ClampsValuesOutsideTheScale()
    {
        var points = ChartGeometry.ToPoints([-3, 20], 10, 100, 50);

        Assert.Equal("0,50 100,0", points);
    }

    [Fact]
    public void X_WithASinglePoint_PlacesItOnTheRight()
    {
        Assert.Equal(600, ChartGeometry.X(0, 1, 600));
    }
}
