using Ortilo.Core.Models;
using Ortilo.Core.Services;

namespace Ortilo.Core.Tests;

public class FocusNavigatorTests
{
    private static readonly WindowPlacement[] Placements =
    [
        new(10, 0, 0, 800, 600),
        new(30, 0, 0, 800, 600),
        new(20, 0, 0, 800, 600)
    ];

    [Theory]
    [InlineData(LayoutType.Tabbed, FocusDirection.Left, 10)]
    [InlineData(LayoutType.Tabbed, FocusDirection.Right, 20)]
    [InlineData(LayoutType.Stacking, FocusDirection.Up, 10)]
    [InlineData(LayoutType.Stacking, FocusDirection.Down, 20)]
    public void FindNextInHeader_FollowsHeaderOrder(LayoutType layout, FocusDirection direction, int expected)
    {
        Assert.Equal((nint)expected, FocusNavigator.FindNextInHeader(Placements, 30, layout, direction));
    }

    [Theory]
    [InlineData(LayoutType.Tabbed, FocusDirection.Left, 10, 20)]
    [InlineData(LayoutType.Tabbed, FocusDirection.Right, 20, 10)]
    [InlineData(LayoutType.Stacking, FocusDirection.Up, 10, 20)]
    [InlineData(LayoutType.Stacking, FocusDirection.Down, 20, 10)]
    public void FindNextInHeader_StopsAtEdgeUntilWrappingIsRequested(
        LayoutType layout, FocusDirection direction, int current, int expected)
    {
        Assert.Null(FocusNavigator.FindNextInHeader(Placements, current, layout, direction));
        Assert.Equal((nint)expected,
            FocusNavigator.FindNextInHeader(Placements, current, layout, direction, wrap: true));
    }

    [Theory]
    [InlineData(LayoutType.Tabbed, FocusDirection.Down)]
    [InlineData(LayoutType.Stacking, FocusDirection.Right)]
    [InlineData(LayoutType.Columns, FocusDirection.Right)]
    public void FindNextInHeader_IgnoresDirectionsOutsideTheLayoutAxis(LayoutType layout, FocusDirection direction)
    {
        Assert.Null(FocusNavigator.FindNextInHeader(Placements, 30, layout, direction, wrap: true));
    }

    [Fact]
    public void FindNextInHeader_DoesNotWrapToItselfOrNavigateFromAnExcludedWindow()
    {
        Assert.Null(FocusNavigator.FindNextInHeader([Placements[0]], 10,
            LayoutType.Tabbed, FocusDirection.Right, wrap: true));
        Assert.Null(FocusNavigator.FindNextInHeader(Placements, 99,
            LayoutType.Tabbed, FocusDirection.Right, wrap: true));
    }

    [Fact]
    public void FindNextInHeader_UsesTheNestedHeadersOwnWindowOrder()
    {
        var header = new ContainerHeaderPlacement(Guid.NewGuid(), new(0, 0, 800, 36), LayoutType.Tabbed,
            [30, 10, 20], Guid.NewGuid(), 30);

        Assert.Equal((nint)10, FocusNavigator.FindNextInHeader(header, 30, FocusDirection.Right));
        Assert.Equal((nint)20, FocusNavigator.FindNextInHeader(header, 30, FocusDirection.Left, wrap: true));
        Assert.Null(FocusNavigator.FindNextInHeader(header, 99, FocusDirection.Right));
    }

    [Fact]
    public void FindNext_PrefersAlignedWindowOverCloserDiagonal()
    {
        WindowPlacement[] tiles =
        [
            new(20, 100, 120, 100, 100),
            new(30, 200, 0, 100, 100),
            new(10, 0, 0, 100, 100)
        ];

        Assert.Equal((nint)30, FocusNavigator.FindNext(tiles, 10, FocusDirection.Right));
        Assert.Equal((nint)10, FocusNavigator.FindNext(tiles, 30, FocusDirection.Left));
        Assert.Null(FocusNavigator.FindNext(tiles, 10, FocusDirection.Left));
        Assert.Null(FocusNavigator.FindNext(tiles, 99, FocusDirection.Right));
    }
}
