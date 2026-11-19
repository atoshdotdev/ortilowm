using Tessel.Core.Services;

namespace Tessel.Core.Models;

public sealed class ContainerTree
{
    public ContainerNode Root { get; private init; } =
        ContainerNode.CreateRoot();

    public nint? FocusedWindowHandle { get; private set; }

    public Guid? FocusedNodeId { get; private set; }

    public ContainerNode? FocusedNode =>
        FocusedNodeId is Guid id
            ? FindById(Root, id)
            : null;

    public IReadOnlyList<nint> WindowHandles =>
        Root.GetLeaves()
            .Select(node => node.WindowHandle!.Value)
            .ToArray();

    public ContainerTree()
    {
        FocusedNodeId = Root.Id;
    }

    internal ContainerTree Copy() => new()
    {
        Root = Root.Copy(parent: null),
        FocusedNodeId = FocusedNodeId,
        FocusedWindowHandle = FocusedWindowHandle
    };

    public bool Contains(nint handle)
    {
        return Root.ContainsWindow(handle);
    }

    public bool AddWindow(nint handle)
    {
        if (handle == nint.Zero || Contains(handle))
            return false;

        var focusedNode = FocusedNode;

        bool preserveContainerFocus =
            focusedNode is { IsLeaf: false } &&
            FocusedWindowHandle is nint previous &&
            Contains(previous);

        if (focusedNode?.PendingSplitOrientation is
            ContainerOrientation pendingOrientation)
        {
            if (focusedNode.IsLeaf)
            {
                if (focusedNode.WindowHandle is null)
                {
                    focusedNode.SetWindow(handle);
                }
                else
                {
                    focusedNode.SplitOccupiedLeaf(
                        pendingOrientation,
                        ContainerNode.CreateWindow(handle));
                }
            }
            else
            {
                focusedNode.SplitContainer(
                    pendingOrientation,
                    ContainerNode.CreateWindow(handle));
            }

            FocusedWindowHandle = handle;
            return true;
        }

        var target = focusedNode is
        { IsLeaf: true, WindowHandle: null }
            ? focusedNode
            : FindFirstEmptyLeaf(Root);

        if (target is not null)
        {
            target.SetWindow(handle);
        }
        else
        {
            target = focusedNode is
            { IsLeaf: true, WindowHandle: not null }
                ? focusedNode
                : FocusedWindowHandle is nint focused
                    ? Root.FindWindow(focused)
                    : null;

            target ??= FindFirstLeaf(Root);

            if (target is null)
                return false;

            target.SplitOccupiedLeaf(
                target.Parent?.Orientation ?? ContainerOrientation.Horizontal,
                ContainerNode.CreateWindow(handle),
                automatic: true);
        }

        if (!preserveContainerFocus)
        {
            SetFocusedWindow(handle);
        }

        return true;
    }

    public bool RemoveWindow(nint handle)
    {
        var node = Root.FindWindow(handle);

        if (node is null)
            return false;

        var replacementBranch = node.Parent?.Children.First(child => child != node);
        if (FocusedWindowHandle == handle)
            FocusedWindowHandle = replacementBranch?.GetLeaves().FirstOrDefault()?.WindowHandle;

        node.ClearWindow();
        CollapseEmptyNodes(node);

        if (FocusedWindowHandle is null)
        {
            FocusedWindowHandle = WindowHandles.FirstOrDefault() is var next &&
                                  next != nint.Zero
                ? next
                : null;
        }

        if (FocusedWindowHandle is nint focused &&
            FocusedNode is { IsLeaf: false } container && container.ContainsWindow(focused))
            return true;

        if (FocusedWindowHandle is nint focusedHandle)
            SetFocusedWindow(focusedHandle);
        else
            FocusedNodeId = Root.Id;

        return true;
    }

    public bool FocusWindow(nint handle)
    {
        if (!Contains(handle))
            return false;

        SetFocusedWindow(handle);
        return true;
    }

    public bool FocusParent()
    {
        var focusedNode = FocusedNode;

        if (focusedNode is null)
        {
            if (FocusedWindowHandle is not nint handle)
                return false;

            focusedNode = Root.FindWindow(handle);

            if (focusedNode is null)
                return false;
        }

        var parent = focusedNode.Parent;

        if (parent is null)
            return false;

        FocusedNodeId = parent.Id;

        return true;
    }

    public bool FocusChild()
    {
        var focusedNode = FocusedNode;

        if (focusedNode is null || focusedNode.IsLeaf)
            return false;

        ContainerNode? child = null;

        if (FocusedWindowHandle is nint handle)
        {
            child = focusedNode.Children.FirstOrDefault(
                candidate => candidate.ContainsWindow(handle));
        }

        child ??= focusedNode.Children.FirstOrDefault(
            candidate => candidate.GetLeaves().Any());

        if (child is null)
            return false;

        FocusedNodeId = child.Id;

        if (FocusedWindowHandle is not nint current ||
            !child.ContainsWindow(current))
        {
            FocusedWindowHandle =
                child.GetLeaves().FirstOrDefault()?.WindowHandle;
        }

        return true;
    }

    public bool SplitFocused(ContainerOrientation orientation)
    {
        var target = FocusedNode;

        if (target is null)
            return false;

        target.SetPendingSplitOrientation(orientation);

        return true;
    }

    public bool ToggleFocusedSplit()
    {
        if (FocusedNode is not { } node)
            return false;

        // A window directly in a tabbed or stacked group uses the group's split toggle.
        if (node.IsLeaf && FindLayoutTarget(node) is { IsSplit: true } group &&
            group.GroupLayout is LayoutType.Tabbed or LayoutType.Stacking)
            node = group;

        var current = node.Orientation ?? node.PendingSplitOrientation ??
            node.Parent?.Orientation ?? ContainerOrientation.Horizontal;
        var next = current == ContainerOrientation.Horizontal
            ? ContainerOrientation.Vertical
            : ContainerOrientation.Horizontal;

        if (node.IsSplit)
            node.RotateSplit(next);
        else
        {
            node.SetGroupLayout(LayoutType.ContainerTree);
            node.SetPendingSplitOrientation(next);
        }

        return true;
    }

    public bool SetFocusedGroupLayout(LayoutType layout)
    {
        if (layout is not (LayoutType.Tabbed or LayoutType.Stacking))
            return false;

        if (FocusedNode is not { } focused)
            return false;

        FindLayoutTarget(focused).SetGroupLayout(layout);
        return true;
    }

    public bool RestoreFocusedGroupSplit()
    {
        var node = FocusedNode;
        if (node is { IsLeaf: true })
        {
            while (node.GroupLayout == LayoutType.ContainerTree && node.Parent is { } parent)
                node = parent;
        }

        if (node is null || node.GroupLayout == LayoutType.ContainerTree)
            return false;

        node.SetGroupLayout(LayoutType.ContainerTree);
        return true;
    }

    private static ContainerNode FindLayoutTarget(ContainerNode node)
    {
        if (!node.IsLeaf)
            return node;

        node = node.Parent ?? node;
        while (node is { IsImplicitSplit: true, GroupLayout: LayoutType.ContainerTree, Parent: { } parent } &&
               node.Orientation == parent.Orientation)
            node = parent;

        return node;
    }

    public bool ContainsWindow(Guid containerId, nint handle) =>
        FindById(Root, containerId)?.ContainsWindow(handle) == true;

    public bool SetSplitRatio(Guid splitId, double ratio)
    {
        var split = FindById(Root, splitId);

        if (split is null || !split.IsSplit)
            return false;

        split.SetRatio(ratio);
        return true;
    }

    public bool MoveWindow(
        nint handle,
        nint targetHandle,
        ContainerOrientation orientation
    )
    {
        return MoveWindow(
            handle,
            targetHandle,
            orientation,
            secondFirst: false);
    }

    public bool MoveWindow(
        nint handle,
        nint targetHandle,
        FocusDirection direction)
    {
        ContainerOrientation orientation = GetOrientation(direction);

        bool insertBefore = direction is
            FocusDirection.Left or FocusDirection.Up;

        return MoveWindow(
            handle,
            targetHandle,
            orientation,
            insertBefore);
    }

    private bool MoveWindow(
        nint handle,
        nint targetHandle,
        ContainerOrientation orientation,
        bool secondFirst
    )
    {
        if (!Enum.IsDefined(orientation))
            throw new ArgumentOutOfRangeException(nameof(orientation));

        if (handle == targetHandle)
            return false;

        var source = Root.FindWindow(handle);
        var target = Root.FindWindow(targetHandle);

        if (source is null || target is null)
            return false;

        var sourceParent = source.Parent;

        if (sourceParent is null)
            return false;

        sourceParent.DetachChild(source);

        var remainingChild = sourceParent.Children[0];
        sourceParent.CollapseTo(remainingChild);

        CollapseEmptyNodes(sourceParent);

        target = Root.FindWindow(targetHandle);

        if (target is null)
            return false;

        target.SplitOccupiedLeaf(
            orientation,
            source,
            secondFirst: secondFirst);

        SetFocusedWindow(handle);

        return true;
    }

    private void CollapseEmptyNodes(ContainerNode node)
    {
        while (node.Parent is not null)
        {
            if (!node.IsLeaf || node.WindowHandle is not null)
                return;

            var parent = node.Parent;

            var sibling = parent.Children.First(
                child => !ReferenceEquals(child, node));

            if (FocusedNodeId == sibling.Id)
                FocusedNodeId = parent.Id;

            parent.CollapseTo(sibling);

            node = parent;
        }
    }

    private static ContainerNode? FindFirstEmptyLeaf(
        ContainerNode node)
    {
        if (node.IsLeaf)
            return node.WindowHandle is null ? node : null;

        foreach (var child in node.Children)
        {
            var result = FindFirstEmptyLeaf(child);

            if (result is not null)
                return result;
        }

        return null;
    }

    private static ContainerNode? FindFirstLeaf(
        ContainerNode node)
    {
        if (node.IsLeaf)
            return node;

        foreach (var child in node.Children)
        {
            var result = FindFirstLeaf(child);

            if (result is not null)
                return result;
        }

        return null;
    }

    private static ContainerNode? FindById(
        ContainerNode node,
        Guid id)
    {
        if (node.Id == id)
            return node;

        foreach (var child in node.Children)
        {
            var result = FindById(child, id);

            if (result is not null)
                return result;
        }

        return null;
    }

    public bool SwapWindows(nint firstHandle, nint secondHandle)
    {
        if (firstHandle == secondHandle)
            return false;

        var first = Root.FindWindow(firstHandle);
        var second = Root.FindWindow(secondHandle);

        if (first is null || second is null)
            return false;

        first.SwapWindowHandles(second);

        if (FocusedWindowHandle is nint focused)
            SetFocusedWindow(focused);

        return true;
    }

    public nint? FindAdjacentWindow(nint handle, FocusDirection direction)
    {
        var placements = ContainerLayoutEngine.Calculate(this, new LayoutArea(0, 0, 100_000, 100_000));
        return FindAdjacentWindow(handle, direction, placements);
    }

    public nint? FindAdjacentWindow(
        nint handle,
        FocusDirection direction,
        IReadOnlyList<WindowPlacement> placements)
    {
        var current = placements.FirstOrDefault(placement => placement.Handle == handle);
        if (current is null)
            return null;

        var next = placements
            .Where(candidate => candidate.Handle != handle)
            .Select(candidate => new
            {
                candidate.Handle,
                EdgeDistance = GetEdgeDistance(current, candidate, direction),
                Overlaps = HasPerpendicularOverlap(current, candidate, direction),
                CenterDistance = GetPerpendicularCenterDistance(current, candidate, direction)
            })
            .Where(candidate => candidate.EdgeDistance >= 0)
            .OrderByDescending(candidate => candidate.Overlaps)
            .ThenBy(candidate => candidate.EdgeDistance)
            .ThenBy(candidate => candidate.CenterDistance)
            .FirstOrDefault();

        return next?.Handle;
    }

    private static long GetEdgeDistance(
        WindowPlacement current, WindowPlacement candidate, FocusDirection direction) =>
        direction switch
        {
            FocusDirection.Left => (long)current.X - candidate.X - candidate.Width,
            FocusDirection.Right => (long)candidate.X - current.X - current.Width,
            FocusDirection.Up => (long)current.Y - candidate.Y - candidate.Height,
            FocusDirection.Down => (long)candidate.Y - current.Y - current.Height,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private static bool HasPerpendicularOverlap(
        WindowPlacement current, WindowPlacement candidate, FocusDirection direction)
    {
        if (direction is FocusDirection.Left or FocusDirection.Right)
            return current.Y < (long)candidate.Y + candidate.Height &&
                candidate.Y < (long)current.Y + current.Height;

        return current.X < (long)candidate.X + candidate.Width &&
            candidate.X < (long)current.X + current.Width;
    }

    private static double GetPerpendicularCenterDistance(
        WindowPlacement current, WindowPlacement candidate, FocusDirection direction) =>
        direction is FocusDirection.Left or FocusDirection.Right
            ? Math.Abs(current.Y + current.Height / 2.0 - candidate.Y - candidate.Height / 2.0)
            : Math.Abs(current.X + current.Width / 2.0 - candidate.X - candidate.Width / 2.0);

    public bool ResizeFocusedSplit(
        FocusDirection direction,
        double amount = 0.05,
        IReadOnlySet<nint>? includedHandles = null
    )
    {
        if (FocusedNode is not { } node || FocusedWindowHandle is null)
            return false;

        if (double.IsNaN(amount) ||
            double.IsInfinity(amount) ||
            amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        ContainerOrientation requiredOrientation = GetOrientation(direction);

        while (node.Parent is not null)
        {
            var parent = node.Parent;

            if (parent.Orientation == requiredOrientation &&
                ContainerLayoutEngine.GetVisibleSplitRatio(parent, includedHandles) is double ratio)
            {
                double adjustment = direction switch
                {
                    FocusDirection.Left or FocusDirection.Up => -amount,
                    FocusDirection.Right or FocusDirection.Down => amount,
                    _ => 0
                };

                double newRatio = Math.Clamp(
                    ratio + adjustment,
                    0.1,
                    0.9
                );

                if (newRatio == ratio)
                    return false;

                parent.SetRatio(newRatio);

                return true;
            }

            node = parent;
        }

        return false;
    }

    public bool ResizeWindowFromDrag(nint handle, LayoutArea before, LayoutArea after,
        IReadOnlyList<ContainerPlacement> placements)
    {
        if (Root.FindWindow(handle) is not { } leaf ||
            placements.FirstOrDefault(placement => placement.NodeId == leaf.Id) is not { } tile)
            return false;

        bool resized = false;
        if (before.Width != after.Width)
        {
            resized |= ResizeEdge(FocusDirection.Left, after.X - before.X);
            resized |= ResizeEdge(FocusDirection.Right, (after.X + after.Width) - (before.X + before.Width));
        }
        if (before.Height != after.Height)
        {
            resized |= ResizeEdge(FocusDirection.Up, after.Y - before.Y);
            resized |= ResizeEdge(FocusDirection.Down, (after.Y + after.Height) - (before.Y + before.Height));
        }
        return resized;

        bool ResizeEdge(FocusDirection edge, int delta)
        {
            if (delta == 0)
                return false;

            var orientation = GetOrientation(edge);
            bool horizontal = orientation == ContainerOrientation.Horizontal;
            bool trailingEdge = edge is FocusDirection.Right or FocusDirection.Down;
            for (var child = leaf; child.Parent is { } split; child = split)
            {
                if (!split.IsSplit || split.GroupLayout != LayoutType.ContainerTree ||
                    split.Orientation != orientation || child != split.Children[trailingEdge ? 0 : 1])
                    continue;

                if (placements.FirstOrDefault(item => item.NodeId == split.Children[0].Id) is not { } first ||
                    !placements.Any(item => item.NodeId == split.Children[1].Id) ||
                    placements.FirstOrDefault(item => item.NodeId == split.Id) is not { } parent)
                    continue;

                int firstSize = horizontal ? first.Area.Width : first.Area.Height;
                int totalSize = horizontal ? parent.Area.Width : parent.Area.Height;
                int divider = horizontal ? first.Area.X + firstSize : first.Area.Y + firstSize;
                int tileEdge = horizontal
                    ? tile.Area.X + (trailingEdge ? tile.Area.Width : 0)
                    : tile.Area.Y + (trailingEdge ? tile.Area.Height : 0);
                if (tileEdge != divider || totalSize <= 0)
                    continue;

                double ratio = Math.Clamp((firstSize + (double)delta) / totalSize, 0.1, 0.9);
                if (Math.Round(totalSize * ratio, MidpointRounding.AwayFromZero) == firstSize)
                    return false;

                split.SetRatio(ratio);
                return true;
            }

            return false;
        }
    }

    private static ContainerOrientation GetOrientation(FocusDirection direction) =>
        direction switch
        {
            FocusDirection.Left or FocusDirection.Right => ContainerOrientation.Horizontal,
            FocusDirection.Up or FocusDirection.Down => ContainerOrientation.Vertical,
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

    private void SetFocusedWindow(nint handle)
    {
        FocusedWindowHandle = handle;
        var node = Root.FindWindow(handle);
        FocusedNodeId = node?.Id;
        for (; node is not null; node = node.Parent)
            node.LastFocusedWindow = handle;
    }

    public void PrintDebugTree()
    {
        PrintNode(Root, 0);

        Console.WriteLine(
            $"Focused node: {FocusedNodeId}");

        Console.WriteLine(
            $"Focused window: {FocusedWindowHandle}");
    }

    private static void PrintNode(ContainerNode node, int depth)
    {
        string indent = new(' ', depth * 2);

        string description = node.IsLeaf
            ? $"Window: {node.WindowHandle}"
            : $"Split: {node.Orientation}, Ratio: {node.Ratio}";

        Console.WriteLine(
            $"{indent}{description} | Id: {node.Id} | " +
            $"Pending: {node.PendingSplitOrientation}");

        foreach (var child in node.Children)
            PrintNode(child, depth + 1);
    }

    public bool MoveWindowToContainer(
        nint handle,
        Guid targetContainerId,
        ContainerOrientation orientation,
        bool insertBefore = false
    ) => MoveWindowToContainer(handle, targetContainerId, orientation, insertBefore, automatic: false);

    private bool MoveWindowToContainer(
        nint handle, Guid targetContainerId, ContainerOrientation orientation, bool insertBefore, bool automatic)
    {
        if (handle == nint.Zero)
            return false;

        if (!Enum.IsDefined(orientation))
            throw new ArgumentOutOfRangeException(nameof(orientation));

        var source = Root.FindWindow(handle);
        var target = FindById(Root, targetContainerId);

        if (source is null || target is null || target is { IsLeaf: true, WindowHandle: null })
            return false;

        if (target.ContainsWindow(handle))
            return false;

        var sourceParent = source.Parent;

        if (sourceParent is null)
            return false;

        sourceParent.DetachChild(source);

        if (target.IsLeaf)
        {
            target.SplitOccupiedLeaf(
                orientation,
                source,
                secondFirst: insertBefore,
                automatic: automatic);
        }
        else
        {
            target.SplitContainer(
                orientation,
                source,
                secondFirst: insertBefore,
                automatic: automatic);
        }

        if (sourceParent.Children.Count == 1)
        {
            var remainingChild = sourceParent.Children[0];
            sourceParent.CollapseTo(remainingChild);
        }

        SetFocusedWindow(handle);

        return true;
    }

    public bool MoveWindowInDirection(nint handle, nint targetHandle, FocusDirection direction)
    {
        var source = Root.FindWindow(handle);
        var target = Root.FindWindow(targetHandle);
        if (source?.Parent is not { } parent || target is null || source == target)
            return false;

        var orientation = GetOrientation(direction);
        if (target.Parent == parent && parent.Orientation == orientation)
        {
            parent.ReverseChildren();
            SetFocusedWindow(handle);
            return true;
        }

        var destinationOrientation = target.Parent != parent ? target.Parent?.Orientation : null;
        return MoveWindowToContainer(handle, target.Id,
            destinationOrientation ?? orientation,
            insertBefore: direction is FocusDirection.Left or FocusDirection.Up,
            automatic: destinationOrientation.HasValue);
    }

    public bool DropWindowAtEdge(nint handle, nint targetHandle, FocusDirection edge)
    {
        var source = Root.FindWindow(handle);
        var target = Root.FindWindow(targetHandle);
        if (source?.Parent is not { } parent || target is null || source == target)
            return false;

        var orientation = GetOrientation(edge);
        bool before = edge is FocusDirection.Left or FocusDirection.Up;
        if (target.Parent == parent && parent.Orientation == orientation)
        {
            if (ReferenceEquals(parent.Children[0], source) == before)
                return false;

            parent.ReverseChildren();
            SetFocusedWindow(handle);
            return true;
        }

        return MoveWindowToContainer(handle, target.Id, orientation, before,
            automatic: target.Parent?.Orientation == orientation);
    }

    public bool DropWindowOnTab(nint handle, Guid groupId, nint targetHandle, bool insertBefore = false)
    {
        var group = FindById(Root, groupId);
        if (group is not { IsSplit: true } || handle == targetHandle ||
            group.GroupLayout is not (LayoutType.Tabbed or LayoutType.Stacking) ||
            !group.ContainsWindow(targetHandle))
            return false;

        var branches = ContainerLayoutEngine.GetGroupChildren(group, group.Orientation).ToArray();
        int targetIndex = Array.FindIndex(branches, branch => branch.ContainsWindow(targetHandle));
        int sourceIndex = Array.FindIndex(branches, branch => branch.ContainsWindow(handle));
        if (sourceIndex >= 0)
        {
            if (sourceIndex == targetIndex)
                return false;

            int destination = targetIndex + (insertBefore ? 0 : 1) - (sourceIndex < targetIndex ? 1 : 0);
            if (destination == sourceIndex)
                return false;

            // Shift the entire entry through the existing tab slots. Split
            // branches keep their nodes, geometry, and internal focus history.
            var source = branches[sourceIndex];
            int step = Math.Sign(destination - sourceIndex);
            for (int index = sourceIndex; index != destination; index += step)
                ContainerNode.SwapChildPositions(source, branches[index + step]);

            SetFocusedWindow(handle);
            return true;
        }

        var branch = branches[targetIndex];
        var orientation = group.Orientation ?? ContainerOrientation.Horizontal;
        if (branch.IsLeaf)
        {
            var edge = orientation == ContainerOrientation.Horizontal
                ? (insertBefore ? FocusDirection.Left : FocusDirection.Right)
                : (insertBefore ? FocusDirection.Up : FocusDirection.Down);
            return DropWindowAtEdge(handle, targetHandle, edge);
        }

        return MoveWindowToContainer(handle, branch.Id, orientation, insertBefore, automatic: true);
    }

    public bool SelectWindow(nint handle)
    {
        if (!Contains(handle))
            return false;

        FocusedWindowHandle = handle;

        return true;
    }
}
