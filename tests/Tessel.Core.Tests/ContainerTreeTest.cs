using Tessel.Core.Models;
using Tessel.Core.Services;

namespace Tessel.Core.Tests;

public class ContainerTreeTests
{
    [Theory]
    [InlineData(ContainerOrientation.Horizontal, 1)]
    [InlineData(ContainerOrientation.Horizontal, 2)]
    [InlineData(ContainerOrientation.Vertical, 1)]
    [InlineData(ContainerOrientation.Vertical, 2)]
    public void ResizeWindowFromDrag_MovesTheSharedDividerFromEitherSide(ContainerOrientation orientation, int handle)
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.SplitFocused(orientation);
        tree.AddWindow(2);
        var area = new LayoutArea(0, 0, 1000, 800);
        var layout = ContainerLayoutEngine.CalculateLayout(tree, area);
        var tile = layout.ContainerPlacements.Single(placement => placement.WindowHandle == handle).Area;
        var after = (orientation, handle) switch
        {
            (ContainerOrientation.Horizontal, 1) => tile with { Width = tile.Width + 100 },
            (ContainerOrientation.Horizontal, 2) => tile with { X = tile.X + 100, Width = tile.Width - 100 },
            (ContainerOrientation.Vertical, 1) => tile with { Height = tile.Height + 100 },
            _ => tile with { Y = tile.Y + 100, Height = tile.Height - 100 }
        };
        var focus = tree.FocusedNodeId;

        Assert.True(tree.ResizeWindowFromDrag(handle, tile, after, layout.ContainerPlacements));

        var resized = ContainerLayoutEngine.Calculate(tree, area);
        Assert.Equal(orientation == ContainerOrientation.Horizontal ? 600 : 500,
            orientation == ContainerOrientation.Horizontal ? resized[0].Width : resized[0].Height);
        Assert.Equal(focus, tree.FocusedNodeId);
    }

    [Fact]
    public void ResizeWindowFromDrag_CornerUpdatesBothNestedDividers()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.FocusWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        var area = new LayoutArea(100, 200, 1000, 800);
        var layout = ContainerLayoutEngine.CalculateLayout(tree, area);
        var tile = layout.ContainerPlacements.Single(placement => placement.WindowHandle == 2).Area;
        var after = tile with { X = tile.X + 100, Width = tile.Width - 100, Height = tile.Height + 80 };

        Assert.True(tree.ResizeWindowFromDrag(2, tile, after, layout.ContainerPlacements));

        var resized = ContainerLayoutEngine.Calculate(tree, area);
        Assert.Equal(new WindowPlacement(2, 700, 200, 400, 480), resized.Single(tile => tile.Handle == 2));
        Assert.Equal(new WindowPlacement(3, 700, 680, 400, 320), resized.Single(tile => tile.Handle == 3));
    }

    [Fact]
    public void ResizeWindowFromDrag_ClampsDividerAndIgnoresOuterEdgesOrCollapsedSplits()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        var area = new LayoutArea(0, 0, 1000, 800);
        var layout = ContainerLayoutEngine.CalculateLayout(tree, area);
        var tile = layout.ContainerPlacements.Single(placement => placement.WindowHandle == 1).Area;

        Assert.False(tree.ResizeWindowFromDrag(1, tile, tile with { X = -100, Width = 600 }, layout.ContainerPlacements));
        var collapsed = ContainerLayoutEngine.CalculateLayout(tree, area, new HashSet<nint> { 1 });
        Assert.False(tree.ResizeWindowFromDrag(1, area, area with { Width = 800 }, collapsed.ContainerPlacements));
        Assert.Equal(0.5, tree.Root.Ratio);

        Assert.True(tree.ResizeWindowFromDrag(1, tile, tile with { Width = 999 }, layout.ContainerPlacements));
        Assert.Equal(0.9, tree.Root.Ratio);
    }

    [Fact]
    public void DropWindowAtEdge_ReordersSiblingsOnlyWhenNeeded()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        var source = tree.Root.FindWindow(1);

        Assert.False(tree.DropWindowAtEdge(1, 2, FocusDirection.Left));
        Assert.True(tree.DropWindowAtEdge(1, 2, FocusDirection.Right));
        Assert.Equal(new nint[] { 2, 1 }, tree.WindowHandles);
        Assert.Same(source, tree.FocusedNode);
        Assert.False(tree.DropWindowAtEdge(1, 1, FocusDirection.Left));
    }

    [Fact]
    public void DropWindowAtEdge_CanSplitPerpendicularToDestinationGroup()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);

        Assert.True(tree.DropWindowAtEdge(1, 3, FocusDirection.Left));

        Assert.Equal(ContainerOrientation.Vertical, tree.Root.Orientation);
        Assert.Equal(ContainerOrientation.Horizontal, tree.Root.FindWindow(1)!.Parent!.Orientation);
        Assert.Equal(new nint[] { 2, 1, 3 }, tree.WindowHandles);
    }

    [Fact]
    public void DropWindowOnTab_AddsEntryBesideWholeSplitBranch()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        Assert.True(tree.FocusWindow(3));
        tree.SplitFocused(ContainerOrientation.Horizontal);
        tree.AddWindow(4);
        var group = tree.Root.Children[1];
        Assert.True(tree.FocusWindow(4));
        Assert.True(tree.FocusParent());
        Assert.True(tree.FocusParent());
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));

        Assert.True(tree.DropWindowOnTab(1, group.Id, 4));

        var result = ContainerLayoutEngine.CalculateLayout(tree, new LayoutArea(0, 0, 900, 600));
        var header = Assert.Single(result.Headers);
        Assert.Equal(new nint[] { 2, 4, 1 }, header.WindowHandles);
        Assert.Equal((nint)1, Assert.Single(result.WindowPlacements).Handle);
        Assert.Equal(ContainerOrientation.Horizontal, tree.Root.FindWindow(4)!.Parent!.Orientation);
    }

    [Theory]
    [InlineData(LayoutType.Tabbed)]
    [InlineData(LayoutType.Stacking)]
    public void DropWindowOnTab_ReordersAnEntireSplitEntryAndKeepsItSelected(LayoutType layout)
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        var split = tree.Root.FindWindow(3)!.Parent!;
        while (tree.FocusParent()) { }
        Assert.True(tree.SetFocusedGroupLayout(layout));
        var area = new LayoutArea(0, 0, 900, 600);

        Assert.True(tree.DropWindowOnTab(3, tree.Root.Id, 1, insertBefore: true));

        var result = ContainerLayoutEngine.CalculateLayout(tree, area);
        var header = Assert.Single(result.Headers);
        Assert.Equal(new nint[] { 3, 1 }, header.WindowHandles);
        Assert.Equal((nint)3, header.SelectedHandle);
        Assert.Equal(new nint[] { 2, 3 }, result.WindowPlacements.Select(window => window.Handle));
        Assert.Same(split, tree.Root.FindWindow(3)!.Parent);
        Assert.Same(split, tree.Root.FindWindow(2)!.Parent);

        Assert.False(tree.DropWindowOnTab(3, tree.Root.Id, 2));
        Assert.False(tree.DropWindowOnTab(3, tree.Root.Id, 1, insertBefore: true));
    }

    [Fact]
    public void DropWindowOnTab_ShiftsEntriesAcrossImplicitSplitsAndPreservesParentLinks()
    {
        var tree = new ContainerTree();
        for (nint handle = 1; handle <= 4; handle++)
            tree.AddWindow(handle);
        while (tree.FocusParent()) { }
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));
        var movedLeaf = tree.Root.FindWindow(4)!;

        Assert.True(tree.DropWindowOnTab(4, tree.Root.Id, 1, insertBefore: true));

        var result = ContainerLayoutEngine.CalculateLayout(tree, new LayoutArea(0, 0, 900, 600));
        Assert.Equal(new nint[] { 4, 1, 2, 3 }, Assert.Single(result.Headers).WindowHandles);
        Assert.Same(movedLeaf, tree.Root.FindWindow(4));
        Assert.Contains(movedLeaf, movedLeaf.Parent!.Children);
        Assert.Equal((nint)4, tree.FocusedWindowHandle);
    }

    [Theory]
    [InlineData(LayoutType.Tabbed)]
    [InlineData(LayoutType.Stacking)]
    public void ToggleFocusedSplit_WithWindowFocusedExitsItsGroupLayout(LayoutType layout)
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        Assert.True(tree.SetFocusedGroupLayout(layout));
        var leaf = tree.FocusedNode;

        Assert.True(tree.ToggleFocusedSplit());

        Assert.Equal(LayoutType.ContainerTree, tree.Root.GroupLayout);
        Assert.Equal(ContainerOrientation.Vertical, tree.Root.Orientation);
        Assert.Same(leaf, tree.FocusedNode);
        Assert.Null(leaf!.PendingSplitOrientation);
    }

    [Fact]
    public void ToggleFocusedSplit_AppliesToNextWindowAndTogglesAgainBeforeInsertion()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        Assert.True(tree.ToggleFocusedSplit());
        Assert.Equal(ContainerOrientation.Vertical, tree.Root.PendingSplitOrientation);
        Assert.True(tree.ToggleFocusedSplit());
        Assert.Equal(ContainerOrientation.Horizontal, tree.Root.PendingSplitOrientation);
        tree.AddWindow(2);
        Assert.Equal(ContainerOrientation.Horizontal, tree.Root.Orientation);
        Assert.Null(tree.Root.PendingSplitOrientation);
    }

    [Fact]
    public void ToggleFocusedSplit_RotatesImplicitLinksButPreservesExplicitSubcontainer()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.AddWindow(3);
        tree.SplitFocused(ContainerOrientation.Horizontal);
        tree.AddWindow(4);
        var explicitGroup = tree.Root.FindWindow(4)!.Parent!;
        tree.FocusWindow(4);
        while (tree.FocusParent()) { }

        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));
        Assert.True(tree.ToggleFocusedSplit());
        Assert.Equal(LayoutType.ContainerTree, tree.Root.GroupLayout);
        Assert.Equal(ContainerOrientation.Vertical, tree.Root.Orientation);
        Assert.Equal(ContainerOrientation.Vertical, tree.Root.Children[1].Orientation);
        Assert.Equal(ContainerOrientation.Horizontal, explicitGroup.Orientation);
        Assert.Same(explicitGroup, tree.Root.FindWindow(4)!.Parent);
        Assert.Equal((nint)4, tree.FocusedWindowHandle);
    }
    [Fact]
    public void FindAdjacentWindow_UsesOnlyTheRenderedWindowsAndPrefersAnAlignedTile()
    {
        var tree = new ContainerTree();
        for (nint handle = 1; handle <= 4; handle++)
            tree.AddWindow(handle);

        WindowPlacement[] visible =
        [
            new(1, 0, 0, 100, 100),
            new(3, 100, 100, 200, 100),
            new(4, 200, 0, 100, 100)
        ];

        Assert.Equal((nint)4, tree.FindAdjacentWindow(1, FocusDirection.Right, visible));
        Assert.Null(tree.FindAdjacentWindow(2, FocusDirection.Right, visible));
    }

    [Fact]
    public void MoveWindowInDirection_AcrossBranchesReparentsTheMovedLeaf()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        var source = tree.Root.FindWindow(1);

        Assert.True(tree.MoveWindowInDirection(1, 3, FocusDirection.Right));

        Assert.Equal(new nint[] { 2, 3, 1 }, tree.WindowHandles);
        Assert.Same(source, tree.Root.FindWindow(1));
        Assert.Same(source, tree.FocusedNode);
        Assert.Same(tree.Root.FindWindow(3)!.Parent, source!.Parent);
        var placements = ContainerLayoutEngine.Calculate(tree, new LayoutArea(0, 0, 600, 900));
        Assert.Equal(new[] { 0, 300, 600 }, placements.Select(placement => placement.Y));
        Assert.All(placements, placement => Assert.Equal(600, placement.Width));
    }

    [Fact]
    public void MoveWindowInDirection_EnteringTabbedGroupAddsASeparateTab()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow(3);
        tree.FocusWindow(3);
        Assert.True(tree.SetFocusedGroupLayout(LayoutType.Tabbed));

        Assert.True(tree.MoveWindowInDirection(1, 3, FocusDirection.Right));

        var result = ContainerLayoutEngine.CalculateLayout(tree, new LayoutArea(0, 0, 600, 900));
        var header = Assert.Single(result.Headers);
        Assert.Equal(new nint[] { 2, 3, 1 }, header.WindowHandles);
        Assert.Equal((nint)1, header.SelectedHandle);
        Assert.Equal((nint)1, Assert.Single(result.WindowPlacements).Handle);
    }

    [Fact]
    public void ResizeFocusedSplit_WhenParentIsSelectedResizesItsOuterDivider()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.AddWindow(3);
        Assert.True(tree.FocusParent());
        var selected = tree.FocusedNode!;

        Assert.True(tree.ResizeFocusedSplit(FocusDirection.Right));

        Assert.Equal(1.0 / 3 + 0.05, tree.Root.Ratio, 10);
        Assert.Equal(0.5, selected.Ratio);
        Assert.Same(selected, tree.FocusedNode);
    }

    [Fact]
    public void RemoveWindow_OutsideFocusedParentPreservesItsFocusAndPendingSplit()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        tree.AddWindow(3);
        Assert.True(tree.FocusParent());
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));

        Assert.True(tree.RemoveWindow(1));

        Assert.Same(tree.Root, tree.FocusedNode);
        Assert.Equal((nint)3, tree.FocusedWindowHandle);
        Assert.Equal(ContainerOrientation.Vertical, tree.FocusedNode!.PendingSplitOrientation);
    }

    [Fact]
    public void MoveWindow_InvalidSplitOrientationLeavesTheTreeUnchanged()
    {
        var tree = new ContainerTree();
        tree.AddWindow(1);
        tree.AddWindow(2);
        var source = tree.Root.FindWindow(1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            tree.MoveWindow(1, 2, (ContainerOrientation)int.MaxValue));

        Assert.Equal(new nint[] { 1, 2 }, tree.WindowHandles);
        Assert.Same(source, tree.Root.FindWindow(1));
    }

    private static IEnumerable<ContainerNode> GetAllLeaves(
    ContainerNode node)
    {
        if (node.IsLeaf)
        {
            yield return node;
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var leaf in GetAllLeaves(child))
            {
                yield return leaf;
            }
        }
    }

    [Fact]
    public void AddWindow_ToEmptyTree_AddsWindowToRoot()
    {
        var tree = new ContainerTree();

        var result = tree.AddWindow((nint)1);

        Assert.True(result);
        Assert.Equal((nint)1, tree.Root.WindowHandle);
        Assert.Equal((nint)1, tree.FocusedWindowHandle);
        Assert.Equal([(nint)1], tree.WindowHandles);
    }

    [Fact]
    public void AddWindow_ToOccupiedTree_CreatesSplit()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.Root.IsSplit);
        Assert.Equal(ContainerOrientation.Horizontal, tree.Root.Orientation);
        Assert.Equal((nint)1, tree.Root.Children[0].WindowHandle);
        Assert.Equal((nint)2, tree.Root.Children[1].WindowHandle);
        Assert.Same(tree.Root, tree.Root.Children[0].Parent);
        Assert.Same(tree.Root, tree.Root.Children[1].Parent);
    }

    [Fact]
    public void AddWindow_WhenEmptyLeafExists_FillsEmptyLeaf()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.SplitFocused(ContainerOrientation.Vertical);

        var result = tree.AddWindow((nint)2);

        Assert.True(result);
        Assert.Equal((nint)1, tree.Root.Children[0].WindowHandle);
        Assert.Equal((nint)2, tree.Root.Children[1].WindowHandle);
        Assert.Equal((nint)2, tree.FocusedWindowHandle);
    }

    [Fact]
    public void SplitFocused_CreatesEmptyChild()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        var originalRoot = tree.Root;

        Assert.True(
            tree.SplitFocused(ContainerOrientation.Vertical));

        Assert.Same(originalRoot, tree.Root);
        Assert.True(tree.Root.IsLeaf);
        Assert.Equal((nint)1, tree.Root.WindowHandle);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.PendingSplitOrientation);

        Assert.Single(tree.WindowHandles);
    }

    [Fact]
    public void RemoveWindow_FromSplit_CollapsesParent()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var result = tree.RemoveWindow((nint)1);

        Assert.True(result);
        Assert.True(tree.Root.IsLeaf);
        Assert.Equal((nint)2, tree.Root.WindowHandle);
        Assert.Null(tree.Root.Parent);
        Assert.Equal((nint)2, tree.FocusedWindowHandle);
    }

    [Fact]
    public void RemoveWindow_FromNestedSplit_CollapsesOnlyNecessarySplit()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.AddWindow((nint)3);

        var result = tree.RemoveWindow((nint)2);

        Assert.True(result);

        Assert.True(tree.Root.IsSplit);
        Assert.Equal((nint)1, tree.Root.Children[0].WindowHandle);
        Assert.Equal((nint)3, tree.Root.Children[1].WindowHandle);

        Assert.Same(tree.Root, tree.Root.Children[0].Parent);
        Assert.Same(tree.Root, tree.Root.Children[1].Parent);
    }

    [Fact]
    public void MoveWindow_ToAnotherWindow_CreatesSplitAtTarget()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var result = tree.MoveWindow(
            (nint)1,
            (nint)2,
            ContainerOrientation.Vertical);

        Assert.True(result);
        Assert.Contains((nint)1, tree.WindowHandles);
        Assert.Contains((nint)2, tree.WindowHandles);
        Assert.Equal((nint)1, tree.FocusedWindowHandle);

        Assert.True(tree.Root.IsSplit);
        Assert.Equal(ContainerOrientation.Vertical, tree.Root.Orientation);
    }

    [Fact]
    public void AddWindow_DuplicateHandle_ReturnsFalse()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        var result = tree.AddWindow((nint)1);

        Assert.False(result);
        Assert.Single(tree.WindowHandles);
    }

    [Fact]
    public void FocusWindow_UnknownHandle_ReturnsFalse()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        var result = tree.FocusWindow((nint)2);

        Assert.False(result);
        Assert.Equal((nint)1, tree.FocusedWindowHandle);
    }

    [Fact]
    public void SetSplitRatio_UpdatesRatio()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var result = tree.SetSplitRatio(tree.Root.Id, 0.7);

        Assert.True(result);
        Assert.Equal(0.7, tree.Root.Ratio);
    }

    [Fact]
    public void SetSplitRatio_InvalidRatio_Throws()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => tree.SetSplitRatio(tree.Root.Id, 1.0));
    }

    [Fact]
    public void SwapWindows_SwapsPositionsWithoutChangingTreeStructure()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.AddWindow((nint)3);

        var originalRoot = tree.Root;
        var originalOrientation = tree.Root.Orientation;
        var originalRatio = tree.Root.Ratio;

        var result = tree.SwapWindows((nint)1, (nint)3);

        Assert.True(result);

        Assert.Same(originalRoot, tree.Root);
        Assert.Equal(originalOrientation, tree.Root.Orientation);
        Assert.Equal(originalRatio, tree.Root.Ratio);

        Assert.Equal(
            new[] { (nint)3, (nint)2, (nint)1 },
            tree.WindowHandles
        );
    }

    [Fact]
    public void SwapWindows_UnknownHandle_ReturnsFalse()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        var result = tree.SwapWindows((nint)1, (nint)99);

        Assert.False(result);
        Assert.Equal([(nint)1], tree.WindowHandles);
    }

    [Fact]
    public void SwapWindows_PreservesFocusedWindow()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.FocusWindow((nint)1);

        tree.SwapWindows((nint)1, (nint)2);

        Assert.Equal((nint)1, tree.FocusedWindowHandle);
    }

    [Fact]
    public void FindAdjacentWindow_HorizontalSplit_FindsLeftAndRight()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.Equal((nint)2,
            tree.FindAdjacentWindow((nint)1, FocusDirection.Right));

        Assert.Equal((nint)1,
            tree.FindAdjacentWindow((nint)2, FocusDirection.Left));

        Assert.Null(
            tree.FindAdjacentWindow((nint)1, FocusDirection.Left));

        Assert.Null(
            tree.FindAdjacentWindow((nint)2, FocusDirection.Right));
    }

    [Fact]
    public void FindAdjacentWindow_VerticalSplit_FindsUpAndDown()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)2);

        Assert.Equal((nint)2,
            tree.FindAdjacentWindow((nint)1, FocusDirection.Down));

        Assert.Equal((nint)1,
            tree.FindAdjacentWindow((nint)2, FocusDirection.Up));

        Assert.Null(
            tree.FindAdjacentWindow((nint)1, FocusDirection.Up));

        Assert.Null(
            tree.FindAdjacentWindow((nint)2, FocusDirection.Down));
    }

    [Fact]
    public void FindAdjacentWindow_NestedSplit_FindsNeighbouringBranch()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)3);

        Assert.Equal((nint)2,
            tree.FindAdjacentWindow((nint)1, FocusDirection.Right));

        Assert.Equal((nint)1,
            tree.FindAdjacentWindow((nint)3, FocusDirection.Left));
    }

    [Fact]
    public void FindAdjacentWindow_UnknownWindow_ReturnsNull()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        Assert.Null(
            tree.FindAdjacentWindow((nint)99, FocusDirection.Right));
    }

    [Theory]
    [InlineData(FocusDirection.Left, ContainerOrientation.Horizontal)]
    [InlineData(FocusDirection.Right, ContainerOrientation.Horizontal)]
    [InlineData(FocusDirection.Up, ContainerOrientation.Vertical)]
    [InlineData(FocusDirection.Down, ContainerOrientation.Vertical)]
    public void MoveWindow_PreservesOriginalNode(
    FocusDirection direction,
    ContainerOrientation expectedOrientation)
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var originalNode = tree.Root.FindWindow((nint)1);

        bool result = tree.MoveWindow(
            (nint)1,
            (nint)2,
            direction);

        Assert.True(result);

        var movedNode = tree.Root.FindWindow((nint)1);

        Assert.Same(originalNode, movedNode);
        Assert.Equal((nint)1, tree.FocusedWindowHandle);

        var target = tree.Root.FindWindow((nint)2)!;

        Assert.Equal(expectedOrientation, target.Parent!.Orientation);
    }

    [Fact]
    public void MoveWindow_Left_InsertsBeforeTarget()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.MoveWindow(
            (nint)1,
            (nint)2,
            FocusDirection.Left));

        Assert.Equal(
            new[] { (nint)1, (nint)2 },
            tree.WindowHandles);
    }

    [Fact]
    public void MoveWindow_Right_InsertsAfterTarget()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.MoveWindow(
            (nint)1,
            (nint)2,
            FocusDirection.Right));

        Assert.Equal(
            new[] { (nint)2, (nint)1 },
            tree.WindowHandles);
    }

    [Fact]
    public void MoveWindow_FromNestedBranch_CollapsesSourceBranch()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)3);

        var originalNode = tree.Root.FindWindow((nint)2);

        bool result = tree.MoveWindow(
            (nint)2,
            (nint)1,
            FocusDirection.Left);

        Assert.True(result);
        Assert.Same(originalNode, tree.Root.FindWindow((nint)2));

        Assert.Equal(
            new[] { (nint)2, (nint)1, (nint)3 },
            tree.WindowHandles);

        Assert.Equal((nint)2, tree.FocusedWindowHandle);
    }

    [Fact]
    public void MoveWindow_FromRootBranch_PreservesRemainingWindows()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)3);

        bool result = tree.MoveWindow(
            (nint)1,
            (nint)3,
            FocusDirection.Right);

        Assert.True(result);

        Assert.Equal(
            new[] { (nint)2, (nint)3, (nint)1 },
            tree.WindowHandles);

        Assert.Equal((nint)1, tree.FocusedWindowHandle);
    }

    [Fact]
    public void MoveWindow_DoesNotDuplicateOrLoseWindows()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.AddWindow((nint)3);
        tree.AddWindow((nint)4);

        bool result = tree.MoveWindow(
            (nint)1,
            (nint)4,
            FocusDirection.Right);

        Assert.True(result);

        Assert.Equal(4, tree.WindowHandles.Count);
        Assert.Equal(4, tree.WindowHandles.Distinct().Count());

        Assert.Contains((nint)1, tree.WindowHandles);
        Assert.Contains((nint)2, tree.WindowHandles);
        Assert.Contains((nint)3, tree.WindowHandles);
        Assert.Contains((nint)4, tree.WindowHandles);
    }

    [Fact]
    public void ResizeFocusedSplit_IncreasesHorizontalRatio()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);

        Assert.True(tree.ResizeFocusedSplit(FocusDirection.Right));

        Assert.Equal(0.55, tree.Root.Ratio);
    }

    [Fact]
    public void ResizeFocusedSplit_DecreasesHorizontalRatio()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);

        Assert.True(tree.ResizeFocusedSplit(FocusDirection.Left));

        Assert.Equal(0.45, tree.Root.Ratio);
    }

    [Fact]
    public void ResizeFocusedSplit_RespectsMaximumRatio()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);

        for (int i = 0; i < 20; i++)
            tree.ResizeFocusedSplit(FocusDirection.Right);

        Assert.Equal(0.9, tree.Root.Ratio);
    }

    [Fact]
    public void ResizeFocusedSplit_RespectsMinimumRatio()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);

        for (int i = 0; i < 20; i++)
            tree.ResizeFocusedSplit(FocusDirection.Left);

        Assert.Equal(0.1, tree.Root.Ratio);
    }

    [Fact]
    public void ResizeFocusedSplit_WithoutMatchingSplit_ReturnsFalse()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);

        Assert.False(tree.ResizeFocusedSplit(FocusDirection.Up));
    }

    [Fact]
    public void FocusParent_FromWindow_FocusesParentContainer()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var focusedWindow = tree.FocusedWindowHandle;
        var focusedLeaf = tree.FocusedNode;

        Assert.NotNull(focusedLeaf);
        Assert.NotNull(focusedLeaf.Parent);

        bool result = tree.FocusParent();

        Assert.True(result);
        Assert.Equal(focusedLeaf.Parent.Id, tree.FocusedNodeId);
        Assert.Equal(focusedWindow, tree.FocusedWindowHandle);
    }

    [Fact]
    public void FocusParent_FromRoot_ReturnsFalse()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());

        Assert.Same(tree.Root, tree.FocusedNode);

        Assert.False(tree.FocusParent());
    }

    [Fact]
    public void FocusParent_CanNavigateMultipleLevels()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.AddWindow((nint)3);

        var focusedLeaf = tree.FocusedNode;
        Assert.NotNull(focusedLeaf);

        var firstParent = focusedLeaf.Parent;
        Assert.NotNull(firstParent);

        var secondParent = firstParent.Parent;
        Assert.NotNull(secondParent);

        Assert.True(tree.FocusParent());
        Assert.Equal(firstParent.Id, tree.FocusedNodeId);

        Assert.True(tree.FocusParent());
        Assert.Equal(secondParent.Id, tree.FocusedNodeId);
    }

    [Fact]
    public void FocusWindow_RestoresFocusToWindowLeaf()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());

        Assert.True(tree.FocusWindow((nint)1));

        Assert.Equal((nint)1, tree.FocusedWindowHandle);
        Assert.Equal(tree.Root.FindWindow((nint)1)!.Id, tree.FocusedNodeId);
    }

    [Fact]
    public void SplitFocused_WhenParentFocused_SetsPendingOrientation()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());

        var focusedParent = tree.FocusedNode;
        Assert.NotNull(focusedParent);
        Assert.True(focusedParent.IsSplit);

        var originalOrientation = focusedParent.Orientation;
        var originalChildren = focusedParent.Children.ToArray();

        Assert.True(
            tree.SplitFocused(ContainerOrientation.Vertical));

        Assert.Equal(originalOrientation, focusedParent.Orientation);
        Assert.Equal(2, focusedParent.Children.Count);

        Assert.Same(originalChildren[0], focusedParent.Children[0]);
        Assert.Same(originalChildren[1], focusedParent.Children[1]);

        Assert.Equal(
            ContainerOrientation.Vertical,
            focusedParent.PendingSplitOrientation);
    }

    [Fact]
    public void SplitFocused_WhenRootFocused_SetsPendingOrientation()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());
        Assert.Same(tree.Root, tree.FocusedNode);

        var originalChildren = tree.Root.Children.ToArray();

        Assert.True(
            tree.SplitFocused(ContainerOrientation.Vertical));

        Assert.Equal(2, tree.WindowHandles.Count);
        Assert.Equal(2, tree.Root.Children.Count);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.PendingSplitOrientation);

        Assert.Same(originalChildren[0], tree.Root.Children[0]);
        Assert.Same(originalChildren[1], tree.Root.Children[1]);
    }

    [Fact]
    public void SplitFocused_PreservesExistingWindowHandles()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());

        Assert.True(
            tree.SplitFocused(ContainerOrientation.Vertical));

        Assert.Contains((nint)1, tree.WindowHandles);
        Assert.Contains((nint)2, tree.WindowHandles);
    }

    [Fact]
    public void AddWindow_WhenFocusedContainerHasPendingOrientation_CreatesNewChild()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());

        var focusedContainer = tree.FocusedNode;
        Assert.NotNull(focusedContainer);

        Assert.True(
            tree.SplitFocused(ContainerOrientation.Vertical));

        Assert.Equal(
            ContainerOrientation.Vertical,
            focusedContainer.PendingSplitOrientation);

        Assert.True(tree.AddWindow((nint)3));

        Assert.Equal(3, tree.WindowHandles.Count);
        Assert.Equal((nint)3, tree.FocusedWindowHandle);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.Orientation);

        Assert.Equal(2, tree.Root.Children.Count);

        Assert.Contains(
            tree.Root.Children,
            node => node.ContainsWindow((nint)3));

        Assert.Null(tree.Root.PendingSplitOrientation);
    }

    [Fact]
    public void AddWindow_WhenFocusedContainerIsNotEmpty_UsesAvailableEmptyLeaf()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));

        Assert.True(tree.FocusWindow((nint)1));

        Assert.True(tree.AddWindow((nint)3));

        Assert.Contains((nint)3, tree.WindowHandles);
        Assert.Equal((nint)3, tree.FocusedWindowHandle);
        Assert.Equal(3, tree.WindowHandles.Count);
    }

    [Fact]
    public void SplitFocused_SetsPendingOrientationWithoutChangingTree()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        bool result = tree.SplitFocused(
            ContainerOrientation.Vertical);

        Assert.True(result);
        Assert.True(tree.Root.IsLeaf);
        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.FocusedNode!.PendingSplitOrientation);

        Assert.Single(tree.WindowHandles);
    }

    [Fact]
    public void AddWindow_UsesPendingOrientation()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        tree.SplitFocused(ContainerOrientation.Vertical);

        bool result = tree.AddWindow((nint)2);

        Assert.True(result);
        Assert.True(tree.Root.IsSplit);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.Orientation);

        Assert.Equal(2, tree.WindowHandles.Count);

        Assert.Null(tree.Root.PendingSplitOrientation);
    }

    [Fact]
    public void AddWindow_WhenParentHasPendingOrientation_PreservesExistingSubtree()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);
        tree.FocusParent();

        tree.SplitFocused(ContainerOrientation.Vertical);

        bool result = tree.AddWindow((nint)3);

        Assert.True(result);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.Orientation);

        Assert.Equal(2, tree.Root.Children.Count);

        Assert.True(tree.Root.Children[0].IsSplit);

        Assert.Equal(
            ContainerOrientation.Horizontal,
            tree.Root.Children[0].Orientation);

        Assert.Equal(3, tree.WindowHandles.Count);
    }

    [Fact]
    public void MoveWindowToContainer_PreservesTargetSubtree()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);
        tree.FocusParent();

        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)3);

        var target = tree.Root.Children[0];

        Assert.True(target.IsSplit);
        Assert.Equal(
            ContainerOrientation.Horizontal,
            target.Orientation);

        double originalRatio = target.Ratio;

        bool result = tree.MoveWindowToContainer(
            (nint)3,
            target.Id,
            ContainerOrientation.Horizontal);

        Assert.True(result);

        Assert.Equal(3, tree.WindowHandles.Count);
        Assert.Equal(3, tree.WindowHandles.Distinct().Count());

        Assert.Contains((nint)1, tree.WindowHandles);
        Assert.Contains((nint)2, tree.WindowHandles);
        Assert.Contains((nint)3, tree.WindowHandles);

        Assert.True(tree.Root.IsSplit);
        Assert.Equal(
            ContainerOrientation.Horizontal,
            tree.Root.Orientation);

        var originalSubtree = tree.Root.Children[0];

        Assert.Equal(
            ContainerOrientation.Horizontal,
            originalSubtree.Orientation);

        Assert.Equal(originalRatio, originalSubtree.Ratio);
    }

    [Fact]
    public void MoveWindowToContainer_SiblingLeaf_PreservesMovedNode()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var originalNode = tree.Root.FindWindow((nint)1)!;
        var target = tree.Root.FindWindow((nint)2)!;

        bool result = tree.MoveWindowToContainer(
            (nint)1,
            target.Id,
            ContainerOrientation.Vertical);

        Assert.True(result);

        Assert.Same(
            originalNode,
            tree.Root.FindWindow((nint)1));

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.Orientation);

        Assert.Equal(2, tree.WindowHandles.Count);
    }

    [Fact]
    public void MoveWindowToContainer_Ancestor_ReturnsFalse()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        var originalHandles = tree.WindowHandles.ToArray();

        bool result = tree.MoveWindowToContainer(
            (nint)1,
            tree.Root.Id,
            ContainerOrientation.Vertical);

        Assert.False(result);

        Assert.Equal(originalHandles, tree.WindowHandles);
    }

    [Fact]
    public void MoveWindowToContainer_MultipleMoves_PreservesAllWindows()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);
        tree.AddWindow((nint)3);
        tree.AddWindow((nint)4);

        var firstTarget = tree.Root.FindWindow((nint)4)!;

        Assert.True(tree.MoveWindowToContainer(
            (nint)1,
            firstTarget.Id,
            ContainerOrientation.Vertical));

        var secondTarget = tree.Root.FindWindow((nint)2)!;

        Assert.True(tree.MoveWindowToContainer(
            (nint)1,
            secondTarget.Id,
            ContainerOrientation.Horizontal));

        Assert.Equal(4, tree.WindowHandles.Count);
        Assert.Equal(4, tree.WindowHandles.Distinct().Count());

        for (nint handle = 1; handle <= 4; handle++)
            Assert.Contains(handle, tree.WindowHandles);
    }

    [Fact]
    public void MoveWindowToContainer_PreservesDeepNestedLayout()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        tree.FocusWindow((nint)1);
        tree.FocusParent();
        tree.SplitFocused(ContainerOrientation.Vertical);
        tree.AddWindow((nint)3);

        tree.FocusWindow((nint)3);
        tree.SplitFocused(ContainerOrientation.Horizontal);
        tree.AddWindow((nint)4);

        var left = tree.Root.Children[0];
        var right = tree.Root.Children[1];

        Assert.True(left.IsSplit);
        Assert.True(right.IsSplit);

        left.SetRatio(0.4);
        right.SetRatio(0.7);

        var originalRightOrientation = right.Orientation;
        var originalRightRatio = right.Ratio;

        var target = right;

        Assert.True(tree.MoveWindowToContainer(
            (nint)1,
            target.Id,
            ContainerOrientation.Vertical));

        Assert.Equal(4, tree.WindowHandles.Count);
        Assert.Equal(4, tree.WindowHandles.Distinct().Count());

        Assert.Contains((nint)1, tree.WindowHandles);
        Assert.Contains((nint)2, tree.WindowHandles);
        Assert.Contains((nint)3, tree.WindowHandles);
        Assert.Contains((nint)4, tree.WindowHandles);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.Orientation);

        Assert.Equal((nint)2, tree.Root.Children[0].WindowHandle);
        Assert.True(tree.Root.Children[0].IsLeaf);

        var window3 = tree.Root.FindWindow((nint)3)!;
        var window4 = tree.Root.FindWindow((nint)4)!;

        Assert.Same(window3.Parent, window4.Parent);

        var originalRight = window3.Parent!;

        Assert.Equal(
            originalRightOrientation,
            originalRight.Orientation);

        Assert.Equal(
            originalRightRatio,
            originalRight.Ratio);

        Assert.Equal((nint)1, tree.FocusedWindowHandle);
    }

    [Fact]
    public void FocusChild_WhenFocusedNodeIsLeaf_ReturnsFalse()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);

        Assert.False(tree.FocusChild());
    }

    [Fact]
    public void FocusChild_AfterFocusParent_ReturnsToOriginalWindow()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusParent());
        Assert.True(tree.FocusChild());

        Assert.Equal((nint)2, tree.FocusedWindowHandle);
        Assert.Equal(
            tree.Root.FindWindow((nint)2)!.Id,
            tree.FocusedNodeId);
    }

    [Fact]
    public void FocusChild_WhenFocusedWindowIsInSecondChild_FollowsFocusedWindow()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusWindow((nint)2));
        Assert.True(tree.FocusParent());
        Assert.True(tree.FocusChild());

        Assert.Equal((nint)2, tree.FocusedWindowHandle);
        Assert.Equal(
            tree.Root.FindWindow((nint)2)!.Id,
            tree.FocusedNodeId);
    }


    [Fact]
    public void FindAdjacentWindow_FollowsHorizontalSplit()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.Equal(
            (nint)2,
            tree.FindAdjacentWindow((nint)1, FocusDirection.Right));

        Assert.Null(
            tree.FindAdjacentWindow((nint)1, FocusDirection.Left));

        Assert.Null(
            tree.FindAdjacentWindow((nint)1, FocusDirection.Up));

        Assert.Null(
            tree.FindAdjacentWindow((nint)1, FocusDirection.Down));
    }

    [Fact]
    public void FindAdjacentWindow_FollowsNestedVerticalSplit()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusWindow((nint)2));
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));

        tree.AddWindow((nint)3);

        Assert.Equal(
            (nint)3,
            tree.FindAdjacentWindow((nint)2, FocusDirection.Down));

        Assert.Null(
            tree.FindAdjacentWindow((nint)2, FocusDirection.Up));

        Assert.Equal(
            (nint)2,
            tree.FindAdjacentWindow((nint)3, FocusDirection.Up));

        Assert.Null(
            tree.FindAdjacentWindow((nint)3, FocusDirection.Down));
    }

    [Fact]
    public void FindAdjacentWindow_CrossesNestedContainerBoundary()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusWindow((nint)2));
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));

        tree.AddWindow((nint)3);

        Assert.Equal(
            (nint)1,
            tree.FindAdjacentWindow((nint)3, FocusDirection.Left));
    }

    [Fact]
    public void FindAdjacentWindow_BetweenNestedSubtrees_SelectsCorrespondingLeaf()
    {
        var tree = new ContainerTree();

        tree.AddWindow((nint)1);
        tree.AddWindow((nint)2);

        Assert.True(tree.FocusWindow((nint)1));
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));
        tree.AddWindow((nint)3);

        Assert.True(tree.FocusWindow((nint)2));
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));
        tree.AddWindow((nint)4);

        Assert.Equal(
            (nint)2,
            tree.FindAdjacentWindow((nint)1, FocusDirection.Right));

        Assert.Equal(
            (nint)4,
            tree.FindAdjacentWindow((nint)3, FocusDirection.Right));

        Assert.Equal(
            (nint)1,
            tree.FindAdjacentWindow((nint)2, FocusDirection.Left));

        Assert.Equal(
            (nint)3,
            tree.FindAdjacentWindow((nint)4, FocusDirection.Left));
    }
}
