using Tessel.Core.Models;
using Tessel.Core.Services;

namespace Tessel.Core.Tests;

public class LayoutSpacingTests
{
    [Fact]
    public void ApplyInnerGap_SeparatesAdjacentTilesByExactOddGap()
    {
        WindowPlacement[] placements = [new(1, 0, 0, 50, 100), new(2, 50, 0, 50, 100)];

        var result = LayoutSpacing.ApplyInnerGap(placements, 7);

        Assert.Equal(new WindowPlacement(1, 0, 0, 46, 100), result[0]);
        Assert.Equal(new WindowPlacement(2, 53, 0, 47, 100), result[1]);
    }

    [Fact]
    public void ApplyInnerGap_DoesNotShrinkOverlappingTabsOrIsolatedTiles()
    {
        WindowPlacement[] placements = [new(1, 0, 0, 100, 100), new(2, 0, 0, 100, 100)];
        Assert.Equal(placements, LayoutSpacing.ApplyInnerGap(placements, 10));
        Assert.Same(placements, LayoutSpacing.ApplyInnerGap(placements, 0));
    }

    [Fact]
    public void ApplyInnerGap_AlignsHeaderAndTileAlongsideAnotherTile()
    {
        var area = new LayoutArea(500, 0, 500, 36);
        var header = new ContainerHeaderPlacement(Guid.NewGuid(), area, LayoutType.Tabbed, [2], Guid.NewGuid(), 2);
        var layout = new ContainerLayoutResult(
            [new(1, 0, 0, 500, 600), new(2, 500, 36, 500, 564)],
            [new(header.NodeId, new(500, 0, 500, 600), true, null)],
            [header]);

        var result = LayoutSpacing.ApplyInnerGap(layout, 9);

        Assert.Equal(new LayoutArea(504, 0, 496, 36), Assert.Single(result.Headers).Area);
        Assert.Equal(new WindowPlacement(1, 0, 0, 495, 600), result.WindowPlacements[0]);
        Assert.Equal(new WindowPlacement(2, 504, 36, 496, 564), result.WindowPlacements[1]);
        Assert.Same(layout.ContainerPlacements, result.ContainerPlacements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ApplyInnerGap_InsetsHeaderBelowNeighborAndPreservesItsOwnGap(int headerGap)
    {
        var header = new ContainerHeaderPlacement(Guid.NewGuid(), new(0, 200, 800, 36),
            LayoutType.Stacking, [2], Guid.NewGuid(), 2);
        var layout = new ContainerLayoutResult(
            [new(1, 0, 0, 800, 200), new(2, 0, 236 + headerGap, 800, 364 - headerGap)], [], [header]);

        var result = LayoutSpacing.ApplyInnerGap(layout, 9);

        Assert.Equal(new LayoutArea(0, 204, 800, 32), Assert.Single(result.Headers).Area);
        Assert.Equal(195, result.WindowPlacements[0].Height);
        Assert.Equal(layout.WindowPlacements[1], result.WindowPlacements[1]);
    }

    [Fact]
    public void GetHeaderDockEdges_UsesFinalScreenBoundsAndTopBarBottom()
    {
        var monitor = new LayoutArea(-800, 20, 800, 600);

        Assert.Equal(LayoutDockEdges.Left | LayoutDockEdges.Top,
            LayoutSpacing.GetHeaderDockEdges(new(-800, 56, 400, 36), monitor, 56));
        Assert.Equal(LayoutDockEdges.Right | LayoutDockEdges.Bottom,
            LayoutSpacing.GetHeaderDockEdges(new(-400, 584, 400, 36), monitor, 56));
        Assert.Equal(LayoutDockEdges.Left | LayoutDockEdges.Top | LayoutDockEdges.Right,
            LayoutSpacing.GetHeaderDockEdges(new(-800, 20, 800, 36), monitor, null));
        Assert.Equal(LayoutDockEdges.None,
            LayoutSpacing.GetHeaderDockEdges(new(-792, 64, 784, 36), monitor, 56));
    }

    [Fact]
    public void Inset_ClampsOversizedGapsToEmptyArea()
    {
        Assert.Equal(new LayoutArea(20, 15, 0, 0),
            LayoutSpacing.Inset(new LayoutArea(10, 10, 10, 10), 30, 5, 30, 30));
    }

    [Fact]
    public void CalculateLayout_ReservesGapOnlyUnderAVisibleNestedHeader()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));
        var area = new LayoutArea(0, 0, 200, 100);

        var result = ContainerLayoutEngine.CalculateLayout(tree, area, headerRowHeight: 30, headerGap: 6);
        Assert.Equal(30, Assert.Single(result.Headers).Area.Height);
        Assert.Equal(36, Assert.Single(result.WindowPlacements).Y);

        var withoutHeader = ContainerLayoutEngine.CalculateLayout(tree, area, headerGap: 6);
        Assert.Equal(0, Assert.Single(withoutHeader.WindowPlacements).Y);
    }
}
