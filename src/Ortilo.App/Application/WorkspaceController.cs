
using Ortilo.Core.Models;
using Ortilo.Core.Services;
using Ortilo.Platform.Windows.Windows;

namespace Ortilo.App.Application;

public sealed class WorkspaceController
{
    private readonly WindowRegistry _registry;
    private readonly WorkspaceManager _workspaceManager;
    private readonly LayoutManager _layoutManager;
    private readonly LayoutCoordinator _layoutCoordinator;
    public event Action<nint?>? FocusedWindowChanged;

    private bool _isSwitchingWorkspace;

    public bool IsSwitchingWorkspace => _isSwitchingWorkspace;

    public WorkspaceController(
        WindowRegistry registry,
        WorkspaceManager workspaceManager,
        LayoutManager layoutManager,
        LayoutCoordinator layoutCoordinator
    )
    {
        _registry = registry;
        _workspaceManager = workspaceManager;
        _layoutManager = layoutManager;
        _layoutCoordinator = layoutCoordinator;
    }

    public void SwitchTo(int number)
    {
        if (_isSwitchingWorkspace || number is < 1 or > 9)
            return;

        if (_workspaceManager.CurrentWorkspace.Number == number)
            return;

        bool switched = false;
        _isSwitchingWorkspace = true;

        try
        {
            if (!_workspaceManager.SwitchTo(number))
                return;

            RefreshWindowVisibility();

            SynchronizeWindows();

            _layoutManager.SetLayout(
                _workspaceManager.CurrentWorkspace.Layout
            );

            _layoutCoordinator.ApplyLayout();

            switched = true;
        }
        finally
        {
            _isSwitchingWorkspace = false;
        }

        if (!switched)
            return;

        FocusSelectedWindow();

        Console.WriteLine($"Switched to workspace {number}");
    }
    public void SynchronizeWindows()
    {
        _registry.Synchronize(
            WindowEnumerator.GetTopLevelWindows()
        );
    }

    public void RefreshWindowVisibility()
    {
        bool hasManagedMonitors = _workspaceManager.ManagedMonitors.Count != 0;
        var visibleHandles = _workspaceManager.Workspaces
            .Where(workspace => !hasManagedMonitors ||
                _workspaceManager.GetMonitorForWorkspace(workspace.Number)
                is not null)
            .SelectMany(workspace => workspace.WindowHandles)
            .ToHashSet();

        foreach (var workspace in _workspaceManager.Workspaces)
        {
            foreach (var handle in workspace.WindowHandles)
            {
                if (WindowVisibility.IsMinimized(handle))
                    continue;

                if (visibleHandles.Contains(handle))
                {
                    if (!hasManagedMonitors || !WindowVisibility.IsHiddenForLayout(handle))
                        WindowVisibility.ShowWithoutActivate(handle);
                }
                else
                    WindowVisibility.Hide(handle);
            }
        }
    }

    public void SelectOnMonitor(nint monitorHandle, int number)
    {
        if (!_workspaceManager.SetActiveMonitor(monitorHandle))
            return;

        if (_workspaceManager.CurrentWorkspace.Number == number)
        {
            _layoutCoordinator.ApplyLayout();
            FocusSelectedWindow();
            return;
        }

        SwitchTo(number);
    }

    public bool MoveWorkspaceToMonitor(int workspaceNumber, nint monitorHandle)
    {
        if (_isSwitchingWorkspace)
            return false;

        if (!_workspaceManager.MoveWorkspaceToMonitor(
            workspaceNumber,
            monitorHandle))
        {
            Console.WriteLine(
                $"Could not move workspace {workspaceNumber} to monitor."
            );

            return false;
        }

        RefreshWindowVisibility();

        _layoutCoordinator.ApplyLayout();

        FocusSelectedWindow();

        Console.WriteLine(
            $"Moved workspace {workspaceNumber} to monitor 0x{monitorHandle.ToInt64():X}"
        );

        return true;
    }

    public bool MoveWindowToAdjacentWorkspace(nint handle, FocusDirection direction)
    {
        if (_isSwitchingWorkspace || !_workspaceManager.MoveWindowToAdjacentWorkspace(handle, direction))
            return false;

        // Both workspaces are already visible. Reposition the window in its new layout
        // before updating the focus and its outline.
        _layoutCoordinator.ApplyLayout();
        FocusSelectedWindow();
        return true;
    }

    public void MoveFocusedWindowTo(int number)
    {
        var currentWorkspace = _workspaceManager.CurrentWorkspace;
        nint handle = WindowFocuser.GetForegroundHandle();

        if (handle == nint.Zero)
            return;

        if (!currentWorkspace.WindowHandles.Contains(handle))
        {
            Console.WriteLine(
                "Focused window does not belong to this workspace."
            );

            return;
        }

        if (currentWorkspace.Number == number)
            return;

        if (!_workspaceManager.MoveWindowToWorkspace(handle, number))
        {
            Console.WriteLine(
                $"Could not move window to workspace {number}."
            );

            return;
        }

        FocusedWindowChanged?.Invoke(null);

        WindowVisibility.Hide(handle);

        Console.WriteLine(
            $"Moved HWND 0x{handle.ToInt64():X} to workspace {number}"
        );

        _layoutCoordinator.ApplyLayout();

        FocusSelectedWindow();
    }

    public void FocusSelectedWindow()
    {
        EnsureSelectedWindow();

        nint? selected =
            _workspaceManager.CurrentWorkspace.SelectedWindowHandle;

        if (selected is null || selected == nint.Zero)
        {
            Console.WriteLine("[FOCUS] No selected window.");
            FocusedWindowChanged?.Invoke(null);
            return;
        }

        bool success = WindowFocuser.TryFocus(selected.Value);
        nint foreground = WindowFocuser.GetForegroundHandle();

        bool isFocused =
            success && foreground == selected.Value;

        Console.WriteLine(
            $"[FOCUS] HWND: 0x{selected.Value.ToInt64():X} | " +
            $"Success: {success} | " +
            $"Foreground: 0x{foreground.ToInt64():X}"
        );

        FocusedWindowChanged?.Invoke(
            isFocused ? foreground : null
        );
    }

    public void DropFloatingWindow(nint handle)
    {
        nint monitor = MonitorEnumerator.GetMonitorForWindow(handle);
        if (_workspaceManager.ReleaseWindowToUnmanagedMonitor(handle, monitor))
            return;

        if (!_workspaceManager.MoveFloatingWindowToMonitor(handle, monitor))
            return;

        if (WindowFocuser.GetForegroundHandle() == handle)
        {
            _workspaceManager.SetActiveMonitor(monitor);
            _workspaceManager.FocusWindow(handle);
            NativeTopBar.UpdateActiveMonitor(monitor);
        }
    }

    public void DropUnmanagedWindow(nint handle)
    {
        if (_workspaceManager.GetWorkspaceForWindow(handle) is not null ||
            !_registry.TryGet(handle, out var window) || window is null ||
            WindowClassifier.Classify(window) != WindowClassification.Tiled)
            return;

        nint monitor = MonitorEnumerator.GetMonitorForWindow(handle);
        if (!_workspaceManager.SetActiveMonitor(monitor))
            return;

        _workspaceManager.AddWindow(handle);
        if (WindowFocuser.GetForegroundHandle() == handle)
            _workspaceManager.FocusWindow(handle);
        _layoutCoordinator.ApplyLayout();
    }

    private sealed record DropPlan(
        Workspace Source, Workspace Target, nint Monitor,
        (Guid Container, nint Window, bool InsertBefore)? Tab, TileDropTarget? Tile);

    private DropPlan? _previewDrop;
    private IReadOnlyList<WindowPlacement>? _previewBaseline;
    private bool _previewApplied;

    public void PreviewTiledWindowDrop(nint handle, int x, int y)
    {
        var plan = FindDrop(handle, x, y);
        var baseline = _layoutCoordinator.CurrentPlacements;
        if (plan == _previewDrop && ReferenceEquals(baseline, _previewBaseline))
            return;

        _previewDrop = plan;
        _previewBaseline = baseline;
        if (plan is null)
        {
            RestoreDropPreview();
            return;
        }

        var source = plan.Source.CopyForLayoutPreview();
        var target = plan.Source == plan.Target ? source : plan.Target.CopyForLayoutPreview();
        bool transferred = source != target && WorkspaceManager.TransferWindow(source, target, handle);
        bool repositioned = ApplyDropPosition(target, handle, plan);
        if (transferred || repositioned)
        {
            target.SelectedWindowHandle = handle;
            target.LayoutTree.FocusWindow(handle);
            _layoutCoordinator.PreviewDrop(source, target);
            _previewApplied = true;
        }
        else
        {
            RestoreDropPreview();
        }
    }

    public void RestoreDropPreview()
    {
        if (!_previewApplied)
            return;

        EndDropPreview();
        _layoutCoordinator.ApplyLayout();
    }

    public void EndDropPreview()
    {
        _previewDrop = null;
        _previewBaseline = null;
        _previewApplied = false;
    }

    public void DropTiledWindow(nint handle, int x, int y)
    {
        if (_workspaceManager.ReleaseWindowToUnmanagedMonitor(handle, MonitorEnumerator.GetMonitorForWindow(handle)))
        {
            _layoutCoordinator.ApplyLayout();
            return;
        }

        var plan = FindDrop(handle, x, y);
        if (plan is null ||
            (plan.Source != plan.Target && !_workspaceManager.MoveWindowToWorkspace(handle, plan.Target.Number)))
        {
            _layoutCoordinator.ApplyLayout();
            return;
        }

        ApplyDropPosition(plan.Target, handle, plan);
        _workspaceManager.SetActiveMonitor(plan.Monitor);
        _workspaceManager.FocusWindow(handle);
        _layoutCoordinator.ApplyLayout();
        FocusSelectedWindow();
    }

    private DropPlan? FindDrop(nint handle, int x, int y)
    {
        var source = _workspaceManager.GetWorkspaceForWindow(handle);
        if (source is null || source.FloatingWindowHandles.Contains(handle))
            return null;

        var monitor = _workspaceManager.ManagedMonitors.FirstOrDefault(candidate =>
            x >= candidate.Bounds.X && (long)x < (long)candidate.Bounds.X + candidate.Bounds.Width &&
            y >= candidate.Bounds.Y && (long)y < (long)candidate.Bounds.Y + candidate.Bounds.Height);
        if (monitor is null ||
            _workspaceManager.GetWorkspaceForMonitor(monitor.Handle) is not { } target ||
            target.WindowHandles.Any(FullscreenManager.IsFullscreen))
            return null;

        // Hit-test the committed geometry even while the preview is moving windows and headers.
        foreach (var tab in _layoutCoordinator.DropTabs)
        {
            if (tab.Monitor == monitor.Handle && TileDropTarget.Contains(tab.Tile, x, y))
            {
                bool before = tab.Layout == LayoutType.Stacking
                    ? y - tab.Tile.Y < tab.Tile.Height / 2
                    : x - tab.Tile.X < tab.Tile.Width / 2;
                return new(source, target, monitor.Handle, (tab.Container, tab.Tile.Handle, before), null);
            }
        }

        var tile = target.Layout is LayoutType.Tabbed or LayoutType.Stacking ? null :
            TileDropTarget.Find(_layoutCoordinator.CurrentPlacements, x, y);
        if (tile is not null && !target.WindowHandles.Contains(tile.Handle))
            tile = null;
        return new(source, target, monitor.Handle, null, tile);
    }

    private static bool ApplyDropPosition(Workspace target, nint handle, DropPlan plan)
    {
        if (target.Layout == LayoutType.ContainerTree)
        {
            if (plan.Tab is { } tab)
                return target.LayoutTree.DropWindowOnTab(handle, tab.Container, tab.Window, tab.InsertBefore);
            if (plan.Tile is { Edge: { } edge } edgeTile)
                return target.LayoutTree.DropWindowAtEdge(handle, edgeTile.Handle, edge);
            return plan.Tile is { } tile && target.LayoutTree.SwapWindows(handle, tile.Handle);
        }

        var position = plan.Tab is { } headerTab
            ? new TileDropTarget(headerTab.Window, headerTab.InsertBefore ? FocusDirection.Left : FocusDirection.Right)
            : plan.Tile;
        if (position is null)
            return false;

        int current = target.WindowHandles.IndexOf(handle);
        int destination = target.WindowHandles.IndexOf(position.Handle);
        if (current < 0 || destination < 0 || current == destination)
            return false;

        if (position.Edge is null)
        {
            (target.WindowHandles[current], target.WindowHandles[destination]) =
                (target.WindowHandles[destination], target.WindowHandles[current]);
        }
        else
        {
            target.WindowHandles.RemoveAt(current);
            int index = target.WindowHandles.IndexOf(position.Handle);
            bool before = position.Edge is FocusDirection.Left or FocusDirection.Up;
            target.WindowHandles.Insert(before ? index : index + 1, handle);
        }

        return true;
    }

    private void EnsureSelectedWindow()
    {
        var workspace = _workspaceManager.CurrentWorkspace;

        if (workspace.SelectedWindowHandle is nint selected &&
            selected != nint.Zero &&
            workspace.WindowHandles.Contains(selected) &&
            WindowFocuser.IsVisible(selected))
        {
            return;
        }

        foreach (nint handle in workspace.WindowHandles)
        {
            if (!WindowFocuser.IsVisible(handle))
                continue;

            if (_workspaceManager.SelectWindow(handle))
                return;
        }
    }
}
