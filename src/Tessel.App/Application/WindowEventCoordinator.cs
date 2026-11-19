
using Tessel.Core.Models;
using Tessel.Core.Services;
using Tessel.Platform.Windows.Windows;

namespace Tessel.App.Application;

public sealed class WindowEventCoordinator : IDisposable
{
    private readonly WindowRegistry _registry;
    private readonly WorkspaceManager _workspaceManager;
    private readonly LayoutCoordinator _layoutCoordinator;
    private readonly Action _printSummary;
    private readonly WorkspaceController _workspaceController;
    private readonly WindowEventListener _windowEventListener;
    private readonly FocusedWindowBorder _focusedWindowBorder = new();

    private LayoutArea? _dragStartBounds;
    private nint _moveSizeWindow;
    private uint _dragStartDpi;
    private bool _disposed;
    internal bool LiveDragPreview { get; set; } = true;

    public WindowEventCoordinator(
        WindowRegistry registry,
        WorkspaceManager workspaceManager,
        LayoutCoordinator layoutCoordinator,
        Action printSummary,
        WorkspaceController workspaceController,
        WindowEventListener windowEventListener
    )
    {
        _registry = registry;
        _workspaceManager = workspaceManager;
        _layoutCoordinator = layoutCoordinator;
        _printSummary = printSummary;
        _workspaceController = workspaceController;
        _windowEventListener = windowEventListener;

        _windowEventListener.EventReceived += OnWindowEvent;
        _windowEventListener.WindowMoveSizeStarted += OnWindowMoveSizeStarted;
        _windowEventListener.WindowMoveSizeEnded += OnWindowMoveSizeEnded;
        _windowEventListener.WindowLocationChanged += OnWindowLocationChanged;
        _workspaceController.FocusedWindowChanged += OnFocusedWindowChanged;
    }

    private void OnFocusedWindowChanged(nint? handle)
    {
        if (_disposed)
            return;

        if (handle is nint focusedHandle &&
            focusedHandle != nint.Zero &&
            focusedHandle == WindowFocuser.GetForegroundHandle())
        {
            _focusedWindowBorder.SetFocusedWindow(focusedHandle);
        }
        else
        {
            _focusedWindowBorder.Clear();
        }
    }

    internal void ConfigureBorder(BorderConfig config)
    {
        _focusedWindowBorder.Enabled = config.Enabled;
        _focusedWindowBorder.Color = config.GetColorRef();
        _focusedWindowBorder.Thickness = config.Thickness ?? 1;
        nint foreground = WindowFocuser.GetForegroundHandle();
        OnFocusedWindowChanged(_workspaceManager.GetWorkspaceForWindow(foreground) is null ? null : foreground);
    }

    private void OnWindowMoveSizeStarted(nint hwnd)
    {
        if (_disposed || hwnd == nint.Zero)
            return;

        var workspace = _workspaceManager.GetWorkspaceForWindow(hwnd);
        if (FullscreenManager.IsFullscreen(hwnd) ||
            (workspace is null && (!_registry.TryGet(hwnd, out var window) || window is null ||
             WindowClassifier.Classify(window) != WindowClassification.Tiled)))
            return;

        _dragStartBounds = WindowPositioner.GetWindowBounds(hwnd);
        if (_dragStartBounds is null)
            return;

        _moveSizeWindow = hwnd;
        if (workspace is null || workspace.FloatingWindowHandles.Contains(hwnd))
            return;

        _dragStartDpi = WindowDpi.Get(hwnd);
        _layoutCoordinator.DraggedWindow = hwnd;
    }

    private void OnWindowMoveSizeEnded(nint hwnd)
    {
        if (_disposed || hwnd == nint.Zero || hwnd != _moveSizeWindow)
            return;

        bool wasTiled = hwnd == _layoutCoordinator.DraggedWindow;
        _moveSizeWindow = nint.Zero;
        var start = _dragStartBounds;
        _dragStartBounds = null;
        var end = WindowPositioner.GetWindowBounds(hwnd);

        if (wasTiled)
        {
            _layoutCoordinator.DraggedWindow = nint.Zero;
            _workspaceController.EndDropPreview();
        }

        var workspace = _workspaceManager.GetWorkspaceForWindow(hwnd);
        bool moved = start is not null && end is not null && (start.X != end.X || start.Y != end.Y);
        if (workspace is null)
        {
            if (moved)
                _workspaceController.DropUnmanagedWindow(hwnd);
        }
        else if (workspace.FloatingWindowHandles.Contains(hwnd))
        {
            if (moved)
                _workspaceController.DropFloatingWindow(hwnd);
        }
        else if (wasTiled && IsWindowDrag(hwnd, start, end) && end is not null)
        {
            var point = WindowPositioner.GetCursorPosition() ??
                (X: end.X + end.Width / 2, Y: end.Y + end.Height / 2);
            _workspaceController.DropTiledWindow(hwnd, point.X, point.Y);
        }
        else if (wasTiled)
        {
            if (workspace.Layout == LayoutType.ContainerTree && start is not null && end is not null)
                workspace.LayoutTree.ResizeWindowFromDrag(hwnd, start, end, _layoutCoordinator.CurrentContainerPlacements);

            _layoutCoordinator.ApplyLayout();
        }

        if (hwnd == WindowFocuser.GetForegroundHandle())
            OnFocusedWindowChanged(_workspaceManager.GetWorkspaceForWindow(hwnd) is null ? null : hwnd);
    }

    private bool IsWindowDrag(nint hwnd, LayoutArea? start, LayoutArea? current) =>
        start is not null && current is not null &&
        (start.X != current.X || start.Y != current.Y) &&
        (WindowDpi.Get(hwnd) != _dragStartDpi ||
         (start.Width == current.Width && start.Height == current.Height));

    private void OnWindowLocationChanged(nint hwnd)
    {
        if (_disposed ||
            hwnd == nint.Zero ||
            IsInternalWindow(hwnd))
        {
            return;
        }

        if (LiveDragPreview && hwnd == _layoutCoordinator.DraggedWindow &&
            _workspaceManager.GetWorkspaceForWindow(hwnd) is { } draggedWorkspace &&
            !draggedWorkspace.FloatingWindowHandles.Contains(hwnd) &&
            WindowPositioner.GetCursorPosition() is { } point)
        {
            if (IsWindowDrag(hwnd, _dragStartBounds, WindowPositioner.GetWindowBounds(hwnd)))
                _workspaceController.PreviewTiledWindowDrop(hwnd, point.X, point.Y);
            else
                _workspaceController.RestoreDropPreview();
        }

        if (hwnd != WindowFocuser.GetForegroundHandle())
            return;

        if (_workspaceManager.GetWorkspaceForWindow(hwnd) is null)
        {
            _focusedWindowBorder.Clear();
            return;
        }

        _focusedWindowBorder.SetFocusedWindow(hwnd);
    }

    private void OnWindowEvent(WindowEventType eventType, nint hwnd)
    {
        if (_disposed || hwnd == nint.Zero)
            return;

        if (eventType == WindowEventType.Destroyed)
        {
            WindowVisibility.ForgetLayoutHiddenWindow(hwnd);
            if (hwnd == _moveSizeWindow)
            {
                _moveSizeWindow = nint.Zero;
                _dragStartBounds = null;
            }
        }

        if (hwnd == _layoutCoordinator.DraggedWindow && eventType == WindowEventType.Destroyed)
        {
            _layoutCoordinator.DraggedWindow = nint.Zero;
            _dragStartBounds = null;
            _workspaceController.EndDropPreview();
        }

        if (eventType == WindowEventType.Foreground)
        {
            HandleForegroundChanged(hwnd);
            return;
        }

        if (IsInternalWindow(hwnd))
            return;

        if (eventType == WindowEventType.Hidden)
        {
            if (!WindowFocuser.IsVisible(hwnd))
            {
                _focusedWindowBorder.Clear(hwnd);
                if (FullscreenManager.IsFullscreen(hwnd))
                    _layoutCoordinator.ApplyLayout();
            }
            return;
        }

        if (eventType == WindowEventType.Shown)
        {
            var owner = _workspaceManager.GetWorkspaceForWindow(hwnd);

            if (owner is not null)
            {
                if (_workspaceManager.GetMonitorForWorkspace(owner.Number)
                    is null)
                {
                    _workspaceController.SwitchTo(owner.Number);
                }

                if (hwnd == WindowFocuser.GetForegroundHandle())
                    _focusedWindowBorder.SetFocusedWindow(hwnd);
                if (FullscreenManager.IsFullscreen(hwnd))
                    _layoutCoordinator.ApplyLayout();
                return;
            }
        }

        if (!RequiresSynchronization(eventType))
            return;

        if (eventType == WindowEventType.StateChanged &&
            _workspaceManager.GetWorkspaceForWindow(hwnd)?.FloatingWindowHandles.Contains(hwnd) == true)
        {
            if (_registry.TryGet(hwnd, out var window) && window is not null)
                _registry.AddOrUpdate(window with { IsMinimized = WindowVisibility.IsMinimized(hwnd) });
            if (FullscreenManager.IsFullscreen(hwnd))
                _layoutCoordinator.ApplyLayout();
            return;
        }

        var owningWorkspace =
            eventType == WindowEventType.Destroyed
                ? _workspaceManager.GetWorkspaceForWindow(hwnd)
                : null;

        bool wasOnActiveWorkspace =
            owningWorkspace?.Number ==
            _workspaceManager.CurrentWorkspace.Number;

        if (eventType == WindowEventType.Destroyed)
        {
            _focusedWindowBorder.Clear(hwnd);

            _workspaceManager.RemoveWindow(hwnd);

            SynchronizeWindows();
        }
        else
        {
            SynchronizeWindows();
        }

        Console.Write($"[{eventType}] HWND: 0x{hwnd.ToInt64():X} | ");

        _printSummary();
        _layoutCoordinator.ApplyLayout();

        if (eventType == WindowEventType.Destroyed &&
            owningWorkspace is not null &&
            wasOnActiveWorkspace)
        {
            _workspaceController.FocusSelectedWindow();
        }
    }

    private void HandleForegroundChanged(nint hwnd)
    {
        if (IsInternalWindow(hwnd))
            return;

        if (_workspaceController.IsSwitchingWorkspace)
            return;

        _layoutCoordinator.UpdateHeaderForeground(hwnd);
        var owner = _workspaceManager.GetWorkspaceForWindow(hwnd);

        if (owner is null)
        {
            _focusedWindowBorder.Clear();
            _layoutCoordinator.ClearParentFocus();

            if (_workspaceManager.IsWindowVisible(hwnd))
            {
                nint monitorHandle =
                    MonitorEnumerator.GetMonitorForWindow(hwnd);

                _workspaceManager.SetActiveMonitor(monitorHandle);
            }

            return;
        }

        nint? assignedMonitor =
            _workspaceManager.GetMonitorForWorkspace(owner.Number);

        if (assignedMonitor is not null)
        {
            _workspaceManager.SetActiveMonitor(assignedMonitor.Value);
        }
        else
        {
            _workspaceController.SwitchTo(owner.Number);
        }

        if (_workspaceManager.CurrentWorkspace.Number != owner.Number)
            return;

        _workspaceManager.FocusWindow(hwnd);

        if (owner.FloatingWindowHandles.Contains(hwnd))
        {
            _layoutCoordinator.ClearParentFocus();
            NativeTopBar.UpdateActiveMonitor(_workspaceManager.ActiveMonitorHandle);
        }
        else
        {
            _layoutCoordinator.ApplyLayout();
        }

        _focusedWindowBorder.SetFocusedWindow(hwnd);
    }

    private void SynchronizeWindows()
    {
        _registry.Synchronize(
            WindowEnumerator.GetTopLevelWindows()
        );

        nint foregroundHandle = WindowFocuser.GetForegroundHandle();

        foreach (var window in _registry.TiledWindows)
        {
            // A window dragged in from an unmanaged display joins on drop.
            if (window.Handle == _moveSizeWindow)
                continue;

            var owner = _workspaceManager.GetWorkspaceForWindow(
                window.Handle
            );

            if (owner is not null)
                continue;

            nint monitorHandle =
                MonitorEnumerator.GetMonitorForWindow(window.Handle);

            if (!_workspaceManager.IsManagedMonitor(monitorHandle))
                continue;

            // The restored screen position does not determine the workspace for a new tile.
            _workspaceManager.AddWindow(window.Handle);

            if (window.Handle == foregroundHandle)
            {
                _workspaceManager.FocusWindow(window.Handle);
            }
        }
    }

    private static bool IsInternalWindow(nint hwnd)
    {
        return NativeLayoutHeader.IsInternalWindow(hwnd) ||
               NativeTopBar.IsInternalWindow(hwnd) ||
               NativeTrayIcon.IsInternalWindow(hwnd) ||
               FocusedWindowBorder.IsInternalWindow(hwnd);
    }

    private static bool RequiresSynchronization(WindowEventType eventType)
    {
        return eventType is
            WindowEventType.Created or
            WindowEventType.Destroyed or
            WindowEventType.Shown or
            WindowEventType.StateChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _windowEventListener.EventReceived -= OnWindowEvent;
        _windowEventListener.WindowMoveSizeStarted -= OnWindowMoveSizeStarted;
        _windowEventListener.WindowMoveSizeEnded -= OnWindowMoveSizeEnded;
        _windowEventListener.WindowLocationChanged -= OnWindowLocationChanged;
        _workspaceController.FocusedWindowChanged -= OnFocusedWindowChanged;

        _focusedWindowBorder.Dispose();

        _disposed = true;
    }
}
