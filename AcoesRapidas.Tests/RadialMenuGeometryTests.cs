using AcoesRapidas.UI;

namespace AcoesRapidas.Tests;

public class RadialMenuGeometryTests
{
    private const double DeadZone = 50;

    [Theory]
    [InlineData(0, -100, 0)]
    [InlineData(100, 0, 1)]
    [InlineData(0, 100, 2)]
    [InlineData(-100, 0, 3)]
    public void FourSlicesFollowClockwiseOrderFromTop(double dx, double dy, int expected)
    {
        Assert.Equal(expected, RadialMenuGeometry.SliceAt(dx, dy, 4, DeadZone));
    }

    [Fact]
    public void PositionJustLeftOfTopStillSelectsFirstSlice()
    {
        Assert.Equal(0, RadialMenuGeometry.SliceAt(-10, -100, 6, DeadZone));
    }

    [Fact]
    public void InsideDeadZoneSelectsNothing()
    {
        Assert.Null(RadialMenuGeometry.SliceAt(10, 10, 6, DeadZone));
    }

    [Fact]
    public void FarBeyondTheRingStillSelectsByDirection()
    {
        Assert.Equal(1, RadialMenuGeometry.SliceAt(5000, 0, 4, DeadZone));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    public void EverySliceCenterMapsBackToItsIndex(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var (x, y) = RadialMenuGeometry.PointOnCircle(RadialMenuGeometry.SliceCenterAngle(i, count), 100);
            Assert.Equal(i, RadialMenuGeometry.SliceAt(x, y, count, DeadZone));
        }
    }

    [Fact]
    public void CenterIsClampedInsideTheScreen()
    {
        Assert.Equal((200d, 664d), RadialMenuGeometry.ClampCenter(5, 860, 1536, 864, 200));
    }

    [Fact]
    public void CenterIsKeptWhenThereIsRoom()
    {
        Assert.Equal((700d, 400d), RadialMenuGeometry.ClampCenter(700, 400, 1536, 864, 200));
    }
}
