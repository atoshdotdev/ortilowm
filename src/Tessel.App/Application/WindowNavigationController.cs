
using Tessel.Core.Models;
using Tessel.Core.Services;
using Tessel.Platform.Windows.Windows;

namespace Tessel.App.Application;

public sealed class WindowNavigationController
{
    internal double ResizeStep { get; set; } = 0.05;
    private readonly WorkspaceManager _workspaceManager;
    private readonly LayoutCoordinator _layoutCoordinator;
    private readonly WorkspaceController _workspaceController;

    public WindowNavigationController(
        WorkspaceManager workspaceManager,
        LayoutCoordinator layoutCoordinator,
        WorkspaceController workspaceController)
    {
        _workspaceManager = workspaceManager;
        _layoutCoordinator = layoutCoordinator;
        _workspaceController = workspaceController;
    }

    public void FocusWindow(FocusDirection direction)
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        var placements = _layoutCoordinator.CurrentPlacements
            .Where(placement => placement.Width > 0 && placement.Height > 0 &&
                workspace.WindowHandles.Contains(placement.Handle))
            .ToArray();

        if (placements.Length == 0)
        {
            FocusAdjacentMonitor(direction);
            return;
        }

        nint currentHandle = WindowFocuser.GetForegroundHandle();

        if (!placements.Any(p => p.Handle == currentHandle))
        {
            currentHandle = workspace.SelectedWindowHandle ?? nint.Zero;
        }

        if (!placements.Any(p => p.Handle == currentHandle))
        {
            currentHandle = placements[0].Handle;
        }

        nint? nextHandle = workspace.Layout switch
        {
            LayoutType.ContainerTree => FindNextInContainerTree(workspace.LayoutTree, currentHandle, direction, placements),
            LayoutType.Tabbed or LayoutType.Stacking => FocusNavigator.FindNextInHeader(
                placements, currentHandle, workspace.Layout, direction),
            _ => FocusNavigator.FindNext(placements, currentHandle, direction)
        };

        if (nextHandle is null)
        {
            if (FocusAdjacentMonitor(direction))
                return;

            nextHandle = workspace.Layout == LayoutType.ContainerTree
                ? FindNextContainerTab(workspace.LayoutTree, currentHandle, direction, spatial: null, wrap: true)?.Handle
                : FocusNavigator.FindNextInHeader(placements, currentHandle, workspace.Layout, direction, wrap: true);
            if (nextHandle is null)
                return;
        }

        bool success = WindowFocuser.TryFocus(nextHandle.Value);

        if (success)
        {
            _workspaceManager.FocusWindow(nextHandle.Value);
            _layoutCoordinator.ApplyLayout();
        }
    }

    private nint? FindNextInContainerTree(ContainerTree tree, nint currentHandle,
        FocusDirection direction, IReadOnlyList<WindowPlacement> placements)
    {
        var spatial = tree.FindAdjacentWindow(currentHandle, direction, placements);
        return FindNextContainerTab(tree, currentHandle, direction, spatial)?.Handle ?? spatial;
    }

    private (Guid Group, nint Handle)? FindNextContainerTab(ContainerTree tree, nint currentHandle,
        FocusDirection direction, nint? spatial, bool wrap = false)
    {
        // The layout visits parent headers first, so start with the innermost group.
        var headers = _layoutCoordinator.CurrentContainerHeaders;
        for (int index = headers.Count - 1; index >= 0; index--)
        {
            var header = headers[index];
            if (header.SelectedHandle == currentHandle &&
                FocusNavigator.FindNextInHeader(header, currentHandle, direction, wrap) is nint next)
            {
                if (spatial is nint adjacent && tree.ContainsWindow(header.SelectedBranchId, adjacent))
                    return null;

                return (header.NodeId, next);
            }
        }

        return null;
    }

    private bool FocusAdjacentMonitor(FocusDirection direction)
    {
        if (_workspaceManager.GetAdjacentWorkspace(direction) is { } workspace &&
            _workspaceManager.GetMonitorForWorkspace(workspace.Number) is nint monitorHandle)
        {
            _workspaceController.SelectOnMonitor(monitorHandle, workspace.Number);
            return true;
        }

        return false;
    }

    public void MoveWindow(FocusDirection direction)
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        var placements = _layoutCoordinator.CurrentPlacements
            .Where(placement => workspace.WindowHandles.Contains(placement.Handle))
            .ToArray();

        if (placements.Length == 0)
            return;

        nint currentHandle = WindowFocuser.GetForegroundHandle();

        if (!workspace.WindowHandles.Contains(currentHandle) ||
            !placements.Any(p => p.Handle == currentHandle))
        {
            currentHandle = workspace.SelectedWindowHandle ?? nint.Zero;
        }

        if (!placements.Any(p => p.Handle == currentHandle))
            return;

        nint? nextHandle;

        if (workspace.Layout == LayoutType.ContainerTree)
        {
            nextHandle = workspace.LayoutTree.FindAdjacentWindow(
                currentHandle,
                direction,
                placements
            );
            if (FindNextContainerTab(workspace.LayoutTree, currentHandle, direction, nextHandle) is { } tab)
            {
                bool before = direction is FocusDirection.Left or FocusDirection.Up;
                if (workspace.LayoutTree.DropWindowOnTab(currentHandle, tab.Group, tab.Handle, before))
                {
                    _workspaceManager.SelectWindowPreservingContainerFocus(currentHandle);
                    _layoutCoordinator.ApplyLayout();
                    return;
                }
            }
        }
        else
        {
            nextHandle = workspace.Layout is LayoutType.Tabbed or LayoutType.Stacking
                ? FocusNavigator.FindNextInHeader(placements, currentHandle, workspace.Layout, direction)
                : FocusNavigator.FindNext(placements, currentHandle, direction);
        }

        if (nextHandle is null)
        {
            if (_workspaceController.MoveWindowToAdjacentWorkspace(currentHandle, direction))
                return;

            Console.WriteLine($"Cannot move window: {direction}");
            return;
        }

        if (workspace.Layout == LayoutType.ContainerTree)
        {
            bool moved = workspace.LayoutTree.MoveWindowInDirection(
                currentHandle,
                nextHandle.Value,
                direction
            );

            if (!moved)
            {
                Console.WriteLine($"Could not move window: {direction}");
                return;
            }

            _workspaceManager.SelectWindowPreservingContainerFocus(
                currentHandle
            );
        }
        else
        {
            int currentIndex = workspace.WindowHandles.IndexOf(currentHandle);
            int nextIndex = workspace.WindowHandles.IndexOf(nextHandle.Value);

            if (currentIndex < 0 || nextIndex < 0)
                return;

            (workspace.WindowHandles[currentIndex],
             workspace.WindowHandles[nextIndex]) =
                (workspace.WindowHandles[nextIndex],
                 workspace.WindowHandles[currentIndex]);
        }

        Console.WriteLine($"Moved window: {direction}");

        _layoutCoordinator.ApplyLayout();
    }

    public void ResizeWindow(FocusDirection direction)
    {
        var workspace = _workspaceManager.CurrentWorkspace;

        if (workspace.Layout != LayoutType.ContainerTree)
            return;

        bool resized = workspace.LayoutTree.ResizeFocusedSplit(
            direction,
            amount: ResizeStep,
            includedHandles: _layoutCoordinator.CurrentPlacements
                .Select(placement => placement.Handle)
                .ToHashSet()
        );

        if (!resized)
        {
            Console.WriteLine(
                $"No resizable container found: {direction}");
            return;
        }

        _layoutCoordinator.ApplyLayout();
    }

    public void FocusParent()
    {
        var workspace = _workspaceManager.CurrentWorkspace;

        if (workspace.Layout != LayoutType.ContainerTree)
            return;

        if (!workspace.LayoutTree.FocusParent())
            return;

        _layoutCoordinator.ShowParentFocus();
    }

    public void ToggleFocusedSplit()
    {
        var workspace = _workspaceManager.CurrentWorkspace;

        if (workspace.Layout != LayoutType.ContainerTree)
            return;

        if (!workspace.LayoutTree.ToggleFocusedSplit())
            return;

        _layoutCoordinator.ApplyLayout();
    }

    public void FocusChild()
    {
        var workspace = _workspaceManager.CurrentWorkspace;

        if (workspace.Layout != LayoutType.ContainerTree)
            return;

        if (!workspace.LayoutTree.FocusChild())
            return;

        _layoutCoordinator.ApplyLayout();
    }
}
