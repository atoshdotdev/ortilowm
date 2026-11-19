using Tessel.Core.Models;
using Tessel.Core.Services;

namespace Tessel.Core.Tests;

public class ContainerLayoutEngineTests
{
    private static readonly LayoutArea Area = new(100, 200, 1000, 800);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoreFocusedGroupSplit_CanRestoreNestedTabsInEitherOrder(bool restoreOuterFirst)
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        tree.FocusWindow(3);
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));
        Assert.True(tree.FocusParent());
        var inner = tree.FocusedNode!;
        Assert.True(tree.FocusParent());
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));
        Assert.Equal(2, ContainerLayoutEngine.CalculateLayout(tree, Area).Headers.Count);

        if (!restoreOuterFirst)
            Assert.True(tree.FocusWindow(3));

        Assert.True(tree.RestoreFocusedGroupSplit());
        Assert.Equal(restoreOuterFirst ? LayoutType.ContainerTree : LayoutType.Tabbed, tree.Root.GroupLayout);
        Assert.Equal(restoreOuterFirst ? LayoutType.Tabbed : LayoutType.ContainerTree, inner.GroupLayout);
        Assert.Equal((nint)3, tree.FocusedWindowHandle);

        if (restoreOuterFirst)
            Assert.True(tree.FocusChild());
        else
        {
            Assert.True(tree.FocusParent());
            Assert.True(tree.FocusParent());
        }

        Assert.True(tree.RestoreFocusedGroupSplit());
        var restored = ContainerLayoutEngine.CalculateLayout(tree, Area);
        Assert.Empty(restored.Headers);
        Assert.Equal(new nint[] { 1, 2, 3 }, restored.WindowPlacements.Select(window => window.Handle));
        Assert.Equal(ContainerOrientation.Horizontal, tree.Root.Orientation);
        Assert.Equal(ContainerOrientation.Vertical, inner.Orientation);
    }

    [Theory]
    [InlineData(LayoutType.Tabbed, 36)]
    [InlineData(LayoutType.Stacking, 72)]
    public void Calculate_NestedGroupReservesItsOwnHeaderAndShowsOnlyItsSelectedBranch(
        LayoutType layout, int headerHeight)
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        tree.FocusWindow(3);
        Assert.True(tree.SetFocusedGroupLayout(layout));

        var result = ContainerLayoutEngine.CalculateLayout(tree, Area, headerRowHeight: 36);
        var header = Assert.Single(result.Headers);
        Assert.Equal(tree.Root.Children[1].Id, header.NodeId);
        Assert.Equal(new LayoutArea(600, 200, 500, headerHeight), header.Area);
        Assert.Equal(new nint[] { 2, 3 }, header.WindowHandles);
        Assert.Equal((nint)3, header.SelectedHandle);
        AssertPlacement(result.WindowPlacements[0], 1, 100, 200, 500, 800);
        AssertPlacement(result.WindowPlacements[1], 3, 600, 200 + headerHeight, 500, 800 - headerHeight);

        var direction = layout == LayoutType.Tabbed ? FocusDirection.Left : FocusDirection.Up;
        Assert.Equal((nint)2, FocusNavigator.FindNextInHeader(header, 3, direction));
        tree.FocusWindow(2);
        result = ContainerLayoutEngine.CalculateLayout(tree, Area, headerRowHeight: 36);
        Assert.Equal(new nint[] { 1, 2 }, result.WindowPlacements.Select(window => window.Handle));
        Assert.Equal((nint)2, Assert.Single(result.Headers).SelectedHandle);
    }

    [Fact]
    public void Calculate_NestedGroupSkipsMinimizedSelectionAndKeepsHeaderWithinTile()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.AddWindow(3);
        tree.FocusWindow(3);
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Stacking));

        var result = ContainerLayoutEngine.CalculateLayout(tree, new LayoutArea(0, 0, 100, 11),
            new HashSet<nint> { 1, 2 }, headerRowHeight: 36);
        var header = Assert.Single(result.Headers);
        Assert.Equal(new nint[] { 1, 2 }, header.WindowHandles);
        Assert.Equal(10, header.Area.Height);
        var placement = Assert.Single(result.WindowPlacements);
        Assert.Equal(1, placement.Height);
        Assert.Equal(10, placement.Y);
    }

    [Fact]
    public void Calculate_TabSelectionRestoresLastFocusedWindowInSplitBranch()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        tree.FocusWindow(3);
        Assert.True(tree.FocusParent());
        Assert.True(tree.FocusParent());
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));
        tree.FocusWindow(1);

        var header = Assert.Single(ContainerLayoutEngine.CalculateLayout(tree, Area).Headers);
        Assert.Equal(new nint[] { 1, 3 }, header.WindowHandles);

        var target = FocusNavigator.FindNextInHeader(header, 1, FocusDirection.Right);
        Assert.Equal((nint)3, target);
        Assert.True(tree.FocusWindow(target!.Value));

        var result = ContainerLayoutEngine.CalculateLayout(tree, Area);
        Assert.Equal((nint)3, Assert.Single(result.Headers).SelectedHandle);
        Assert.Equal(new nint[] { 2, 3 }, result.WindowPlacements.Select(window => window.Handle));
    }

    [Fact]
    public void Calculate_OrdinaryWindowsShareTheRowEvenly()
    {
        var tree = new ContainerTree();
        for (nint handle = 1; handle <= 4; handle++)
            tree.AddWindow(handle);

        var placements = ContainerLayoutEngine.Calculate(tree, Area);

        Assert.Equal(4, placements.Count);
        Assert.All(placements, placement => Assert.Equal(250, placement.Width));
    }

    [Fact]
    public void Calculate_NewWindowInVerticalGroupInheritsItsOrientation()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(2);
        tree.AddWindow(3);

        var placements = ContainerLayoutEngine.Calculate(tree, new LayoutArea(0, 0, 900, 900));

        Assert.Equal(3, placements.Count);
        Assert.All(placements, placement =>
        {
            Assert.Equal(900, placement.Width);
            Assert.Equal(300, placement.Height);
        });
    }

    [Fact]
    public void Calculate_ImplicitSplitRedistributesSpaceWhenAWindowIsExcluded()
    {
        var tree = new ContainerTree();
        for (nint handle = 1; handle <= 4; handle++)
            tree.AddWindow(handle);

        var placements = ContainerLayoutEngine.Calculate(
            tree, new LayoutArea(0, 0, 900, 600), new HashSet<nint> { 1, 3, 4 });

        Assert.Equal(new nint[] { 1, 3, 4 }, placements.Select(placement => placement.Handle));
        Assert.All(placements, placement => Assert.Equal(300, placement.Width));
    }

    [Fact]
    public void Calculate_WithEmptyTree_ReturnsEmpty()
    {
        var tree = new ContainerTree();

        var result = ContainerLayoutEngine.Calculate(tree, Area);

        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_WithOneWindow_UsesEntireArea()
    {
        var tree = new ContainerTree();
        tree.AddWindow((nint)1);

        var result = ContainerLayoutEngine.Calculate(tree, Area);

        var placement = Assert.Single(result);

        Assert.Equal((nint)1, placement.Handle);
        Assert.Equal(100, placement.X);
        Assert.Equal(200, placement.Y);
        Assert.Equal(1000, placement.Width);
        Assert.Equal(800, placement.Height);
    }

    [Fact]
    public void Calculate_HorizontalSplit_DividesWidth()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var result = ContainerLayoutEngine.Calculate(tree, Area);

        Assert.Equal(2, result.Count);

        AssertPlacement(result[0], 1, 100, 200, 500, 800);
        AssertPlacement(result[1], 2, 600, 200, 500, 800);
    }

    [Fact]
    public void Calculate_VerticalSplit_DividesHeight()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)2);

        var result = ContainerLayoutEngine.Calculate(tree, Area);

        Assert.Equal(2, result.Count);

        AssertPlacement(result[0], 1, 100, 200, 1000, 400);
        AssertPlacement(result[1], 2, 100, 600, 1000, 400);
    }

    [Fact]
    public void Calculate_CustomRatio_UsesFirstChildRatio()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.SetSplitRatio(tree.Root.Id, 0.7);

        var result = ContainerLayoutEngine.Calculate(tree, Area);

        AssertPlacement(result[0], 1, 100, 200, 700, 800);
        AssertPlacement(result[1], 2, 800, 200, 300, 800);
    }

    [Fact]
    public void Calculate_NestedSplits_CalculatesRecursively()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)3);

        var result = ContainerLayoutEngine.Calculate(tree, Area);

        Assert.Equal(3, result.Count);

        AssertPlacement(result[0], 1, 100, 200, 500, 800);
        AssertPlacement(result[1], 2, 600, 200, 500, 400);
        AssertPlacement(result[2], 3, 600, 600, 500, 400);
    }

    [Fact]
    public void Calculate_WithEmptyLeaf_OmitsEmptyLeaf()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.SplitFocused(ContainerOrientation.Vertical);

        var result = ContainerLayoutEngine.Calculate(tree, Area);

        var placement = Assert.Single(result);

        Assert.Equal((nint)1, placement.Handle);
        Assert.Equal(1000, placement.Width);
        Assert.Equal(800, placement.Height);
    }

    [Fact]
    public void Calculate_WithInvalidArea_ReturnsEmpty()
    {
        var tree = new ContainerTree();
        tree.AddWindow((nint)1);

        var result = ContainerLayoutEngine.Calculate(
            tree,
            new LayoutArea(0, 0, 0, 800));

        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_WithOddDimensions_PreservesTotalWidth()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var area = new LayoutArea(0, 0, 1001, 801);

        var result = ContainerLayoutEngine.Calculate(tree, area);

        Assert.Equal(1001, result.Sum(p => p.Width));
        Assert.Equal(501, result[0].Width);
        Assert.Equal(500, result[1].Width);
    }

    private static void AssertPlacement(
        WindowPlacement actual,
        long handle,
        int x,
        int y,
        int width,
        int height)
    {
        Assert.Equal((nint)handle, actual.Handle);
        Assert.Equal(x, actual.X);
        Assert.Equal(y, actual.Y);
        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }

    [Fact]
    public void Calculate_ExcludedWindow_CollapsesItsSplit()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var result = ContainerLayoutEngine.Calculate(
            tree,
            Area,
            new HashSet<nint> { (nint)2 });

        var placement = Assert.Single(result);

        Assert.Equal((nint)2, placement.Handle);
        Assert.Equal(100, placement.X);
        Assert.Equal(200, placement.Y);
        Assert.Equal(1000, placement.Width);
        Assert.Equal(800, placement.Height);
    }

    [Fact]
    public void Calculate_ExcludedWindowInNestedSplit_PreservesRemainingLayout()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.FocusWindow((nint)2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)3);

        var result = ContainerLayoutEngine.Calculate(
            tree,
            Area,
            new HashSet<nint> { (nint)1, (nint)3 });

        Assert.Equal(2, result.Count);

        AssertPlacement(result[0], 1, 100, 200, 500, 800);
        AssertPlacement(result[1], 3, 600, 200, 500, 800);
    }

    [Fact]
    public void CalculateLayout_ReturnsBoundsForRootAndWindowLeaves()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var area = new LayoutArea(0, 0, 1000, 800);

        var result = ContainerLayoutEngine.CalculateLayout(
            tree,
            area);

        Assert.Equal(2, result.WindowPlacements.Count);

        Assert.Contains(
            result.ContainerPlacements,
            p => p.NodeId == tree.Root.Id && p.IsSplit);

        Assert.Equal(
            3,
            result.ContainerPlacements.Count);
    }

    [Fact]
    public void CalculateLayout_ExcludesMinimizedWindowsFromContainerBounds()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var includedHandles = new HashSet<nint>
    {
        (nint)1
    };

        var result = ContainerLayoutEngine.CalculateLayout(
            tree,
            new LayoutArea(0, 0, 1000, 800),
            includedHandles);

        Assert.Single(result.WindowPlacements);

        Assert.All(
            result.ContainerPlacements,
            placement => Assert.Equal(
                new LayoutArea(0, 0, 1000, 800),
                placement.Area));
    }
}
