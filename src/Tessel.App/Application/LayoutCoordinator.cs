using Tessel.Core.Models;
using Tessel.Core.Services;
using Tessel.Platform.Windows.Windows;

namespace Tessel.App.Application;

public sealed class LayoutCoordinator : IDisposable
{
    private readonly WindowRegistry _registry;
    private readonly WorkspaceManager _workspaceManager;
    private readonly FocusedContainerBorder _focusedContainerBorder = new();
    private Workspace? _parentFocusWorkspace;

    private IReadOnlyList<WindowPlacement> _currentPlacements = [];
    internal IReadOnlyList<ContainerPlacement> CurrentContainerPlacements { get; private set; } = [];
    public nint DraggedWindow { get; set; }
    public IReadOnlyList<ContainerHeaderPlacement> CurrentContainerHeaders { get; private set; } = [];
    public IReadOnlyList<(nint Monitor, Guid Container, LayoutType Layout, WindowPlacement Tile)> DropTabs { get; private set; } = [];

    private bool _isApplyingLayout;
    private bool _topBarUnavailable;
    private bool _disposed;
    private GapConfig _gaps = new();
    private bool _showTopBar = true;
    private (uint? Light, uint? Dark) _workspaceIndicatorColors;
    private (uint? Light, uint? Dark) _tabIndicatorColors;

    public IReadOnlyList<WindowPlacement> CurrentPlacements =>
        _currentPlacements;

    public LayoutCoordinator(
        WindowRegistry registry,
        WorkspaceManager workspaceManager)
    {
        _registry = registry;
        _workspaceManager = workspaceManager;
    }

    public void ShowParentFocus()
    {
        _parentFocusWorkspace = _workspaceManager.CurrentWorkspace;
        ApplyLayout();
    }

    internal void Configure(TesselConfig config)
    {
        _gaps = config.Gaps;
        _showTopBar = config.ShowTopBar;
        _workspaceIndicatorColors = config.WorkspaceIndicator.GetColors();
        _tabIndicatorColors = config.TabIndicator.GetColors();
        _topBarUnavailable = false;
        var border = config.ParentBorder;
        _focusedContainerBorder.Enabled = border.Enabled;
        _focusedContainerBorder.Color = border.GetColorRef(0x00459AEE);
        _focusedContainerBorder.Thickness = border.Thickness ?? 2;
        if (!border.Enabled)
            _focusedContainerBorder.Clear();
    }

    public void ClearParentFocus()
    {
        _parentFocusWorkspace = null;
        _focusedContainerBorder.Clear();
    }

    public void UpdateHeaderForeground(nint hwnd)
    {
        var workspace = _workspaceManager.GetWorkspaceForWindow(hwnd);
        bool isTiled = workspace is not null && !workspace.FloatingWindowHandles.Contains(hwnd);
        NativeLayoutHeader.SetForegroundOverlay(isTiled ? nint.Zero : hwnd);
    }

    public void ApplyLayout() => ApplyLayout(preview: null);

    public void PreviewDrop(Workspace source, Workspace target) => ApplyLayout((source, target));

    private void ApplyLayout((Workspace Source, Workspace Target)? preview)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        LayoutArea? focusedContainerArea = null;
        nint parentFocusWindow = nint.Zero;

        if (_isApplyingLayout)
            return;

        _isApplyingLayout = true;

        try
        {
            UpdateHeaderForeground(WindowFocuser.GetForegroundHandle());
            var allPlacements = new List<WindowPlacement>();
            var containerPlacements = new List<ContainerPlacement>();
            var containerHeaders = new List<ContainerHeaderPlacement>();
            var usedHeaders = new HashSet<(nint Monitor, Guid Container)>();
            var tiledWindows = _registry.TiledWindows.ToDictionary(window => window.Handle);

            var monitorHandles = _workspaceManager.ManagedMonitors
                .Select(monitor => monitor.Handle)
                .ToHashSet();
            NativeTopBar.RemoveBarsNotOnMonitors(_showTopBar ? monitorHandles : new HashSet<nint>());
            NativeLayoutHeader.RemoveHeadersNotOnMonitors(monitorHandles);

            foreach (var monitor in _workspaceManager.ManagedMonitors)
            {
                var workspace =
                    _workspaceManager.GetWorkspaceForMonitor(monitor.Handle);

                if (workspace is null)
                {
                    NativeTopBar.Hide(monitor.Handle);
                    NativeLayoutHeader.Hide(monitor.Handle);
                    continue;
                }

                bool isProjected = false;
                if (preview is { } projection)
                {
                    var projected = workspace.Number == projection.Target.Number ? projection.Target :
                        workspace.Number == projection.Source.Number ? projection.Source : null;
                    if (projected is not null)
                    {
                        workspace = projected;
                        isProjected = true;
                    }
                }

                if (workspace.WindowHandles.Any(handle => FullscreenManager.IsFullscreen(handle) &&
                    WindowFocuser.IsVisible(handle) && !WindowVisibility.IsMinimized(handle)))
                {
                    NativeTopBar.Hide(monitor.Handle);
                    continue;
                }

                uint dpi = NativeTopBar.GetPendingDpi(monitor.Handle);
                if (dpi == 0)
                    dpi = NativeLayoutHeader.GetPendingDpi(monitor.Handle);
                if (dpi == 0)
                    dpi = MonitorEnumerator.GetDpi(monitor.Handle);

                int ScaleGap(int gap) => (int)((gap * dpi + 48) / 96);

                int taskbarGap = ScaleGap(_gaps.Taskbar);
                var work = monitor.WorkArea;
                var bounds = monitor.Bounds;
                var area = LayoutSpacing.Inset(work,
                    work.X > bounds.X ? taskbarGap : 0,
                    work.Y > bounds.Y ? taskbarGap : 0,
                    work.X + work.Width < bounds.X + bounds.Width ? taskbarGap : 0,
                    work.Y + work.Height < bounds.Y + bounds.Height ? taskbarGap : 0);

                bool isActiveMonitor =
                    monitor.Handle == _workspaceManager.ActiveMonitorHandle;

                int topBarHeight = 0;
                if (_showTopBar && !_topBarUnavailable)
                {
                    try
                    {
                        var focusedNode =
                            workspace.Layout == LayoutType.ContainerTree
                                ? workspace.LayoutTree.FocusedNode
                                : null;

                        var pending = focusedNode?.PendingSplitOrientation;
                        ContainerOrientation? direction = focusedNode is null ? null :
                            pending ?? focusedNode.Orientation ?? focusedNode.Parent?.Orientation ??
                            ContainerOrientation.Horizontal;

                        topBarHeight = NativeTopBar.Show(
                            monitor.Handle,
                            area,
                            workspace.Number,
                            isActiveMonitor,
                            direction,
                            pending.HasValue,
                            dpi,
                            _workspaceIndicatorColors
                        );
                    }
                    catch (Exception exception)
                    {
                        _topBarUnavailable = true;

                        Console.WriteLine(
                            $"Topbar error: {exception.Message}"
                        );
                    }
                }

                int topBarInset = topBarHeight > 0 ? topBarHeight + ScaleGap(_gaps.TopBar) : 0;
                int outerGap = ScaleGap(_gaps.Outer);
                var layoutArea = LayoutSpacing.Inset(area, outerGap, topBarInset + outerGap, outerGap, outerGap);
                int? topBarBottom = topBarHeight > 0 ? area.Y + topBarHeight : null;
                LayoutDockEdges DockEdges(LayoutArea headerArea) =>
                    LayoutSpacing.GetHeaderDockEdges(headerArea, monitor.Bounds, topBarBottom);

                var windows = workspace.WindowHandles
                    .Where(handle =>
                        !workspace.FloatingWindowHandles.Contains(handle))
                    .Select(handle => tiledWindows.GetValueOrDefault(handle))
                    .Where(window =>
                        window is not null &&
                        !window.IsMinimized &&
                        !WindowVisibility.IsMinimized(window.Handle))
                    .Cast<WindowMetadata>()
                    .ToArray();

                nint foregroundHandle = WindowFocuser.GetForegroundHandle();

                if (preview is null && workspace.WindowHandles.Contains(foregroundHandle) &&
                    !WindowVisibility.IsMinimized(foregroundHandle))
                {
                    workspace.SelectedWindowHandle = foregroundHandle;

                    if (workspace.Layout == LayoutType.ContainerTree &&
                        workspace.LayoutTree.FocusedWindowHandle != foregroundHandle)
                    {
                        workspace.LayoutTree.FocusWindow(foregroundHandle);
                    }
                }

                nint activeHandle =
                    workspace.SelectedWindowHandle ?? nint.Zero;

                if (!windows.Any(window => window.Handle == activeHandle))
                {
                    activeHandle = windows.FirstOrDefault()?.Handle ?? nint.Zero;

                    if ((preview is null || isProjected) &&
                        (workspace.SelectedWindowHandle is not nint selected ||
                         !workspace.FloatingWindowHandles.Contains(selected) ||
                         !WindowFocuser.IsVisible(selected) || WindowVisibility.IsMinimized(selected)))
                    {
                        workspace.SelectedWindowHandle = activeHandle != nint.Zero ? activeHandle : null;
                    }
                }

                bool usesHeader = workspace.Layout is LayoutType.Stacking or LayoutType.Tabbed &&
                    windows.Length > 0;
                int headerHeight = 0;

                if (usesHeader)
                {
                    try
                    {
                        headerHeight = NativeLayoutHeader.Show(
                            monitor.Handle, layoutArea, windows, activeHandle, workspace.Layout, dpi: dpi,
                            dockedEdges: DockEdges(layoutArea) & ~LayoutDockEdges.Bottom,
                            indicatorColors: _tabIndicatorColors);
                        if (headerHeight > 0)
                            usedHeaders.Add((monitor.Handle, Guid.Empty));

                        if (headerHeight > 0)
                            layoutArea = LayoutSpacing.Inset(layoutArea,
                                0, headerHeight + ScaleGap(_gaps.LayoutHeader), 0, 0);
                    }
                    catch (Exception exception)
                    {
                        Console.WriteLine($"Header error: {exception.Message}");
                        NativeLayoutHeader.Hide(monitor.Handle);
                    }
                }
                else
                {
                    NativeLayoutHeader.Hide(monitor.Handle);
                }

                IReadOnlyList<WindowPlacement> placements;

                if (workspace.Layout == LayoutType.ContainerTree)
                {
                    var visibleHandles = windows
                        .Select(window => window.Handle)
                        .ToHashSet();
                    var result = ContainerLayoutEngine.CalculateLayout(
                        workspace.LayoutTree,
                        layoutArea,
                        visibleHandles,
                        NativeLayoutHeader.GetRowHeight(dpi),
                        ScaleGap(_gaps.LayoutHeader)
                    );
                    result = LayoutSpacing.ApplyInnerGap(result, ScaleGap(_gaps.Inner));

                    placements = result.WindowPlacements;
                    if (preview is null)
                        containerPlacements.AddRange(result.ContainerPlacements);
                    containerHeaders.AddRange(result.Headers);

                    foreach (var header in result.Headers)
                    {
                        var entries = header.WindowHandles
                            .Select(handle => windows.First(window => window.Handle == handle))
                            .ToArray();
                        try
                        {
                            if (NativeLayoutHeader.Show(monitor.Handle, header.Area, entries,
                                    header.SelectedHandle, header.Layout, header.NodeId, header.Area.Height, dpi,
                                    dockedEdges: DockEdges(header.Area),
                                    showDivider: header.Area.X == layoutArea.X && header.Area.Y == layoutArea.Y &&
                                        header.Area.Width == layoutArea.Width,
                                    indicatorColors: _tabIndicatorColors) > 0)
                                usedHeaders.Add((monitor.Handle, header.NodeId));
                        }
                        catch (Exception exception)
                        {
                            Console.WriteLine($"Header error: {exception.Message}");
                        }
                    }

                    if (result.Headers.Count > 0)
                    {
                        var placedHandles = placements.Select(placement => placement.Handle).ToHashSet();
                        foreach (var window in windows)
                        {
                            if (window.Handle != DraggedWindow && !placedHandles.Contains(window.Handle))
                                WindowVisibility.HideForLayout(window.Handle);
                        }
                    }

                    if (isActiveMonitor && workspace == _parentFocusWorkspace &&
                        foregroundHandle != nint.Zero &&
                        foregroundHandle == workspace.LayoutTree.FocusedWindowHandle &&
                        workspace.LayoutTree.FocusedNode is { IsLeaf: false } focusedParent)
                    {
                        focusedContainerArea = result.ContainerPlacements
                            .FirstOrDefault(container =>
                                container.NodeId == focusedParent.Id)
                            ?.Area;
                        parentFocusWindow = foregroundHandle;
                    }
                }
                else
                {
                    placements = WindowLayoutEngine.Calculate(
                        windows,
                        layoutArea,
                        workspace.Layout
                    );
                    placements = LayoutSpacing.ApplyInnerGap(placements, ScaleGap(_gaps.Inner));
                }

                allPlacements.AddRange(placements);

                foreach (var placement in placements)
                {
                    if (placement.Handle == DraggedWindow)
                        continue;

                    if (WindowFocuser.IsVisible(placement.Handle))
                        WindowVisibility.ForgetLayoutHiddenWindow(placement.Handle);
                    else
                        WindowVisibility.ShowWithoutActivate(placement.Handle);
                    bool success = WindowPositioner.TryApply(placement, out int win32Error);

                    if (!success && preview is null)
                    {
                        string detail = win32Error switch
                        {
                            0 => "",
                            5 => " Windows denied access. Check whether the target app is running elevated.",
                            _ => $" Win32 error {win32Error}."
                        };
                        Console.Error.WriteLine($"Could not position window 0x{placement.Handle:X}.{detail}");
                    }
                }

            }

            NativeLayoutHeader.RemoveUnusedHeaders(usedHeaders, retain: DraggedWindow != nint.Zero);
            foreach (var (monitor, container) in usedHeaders)
                NativeLayoutHeader.BringToFront(monitor, container);
            if (preview is not null)
                return;

            _currentPlacements = allPlacements;
            CurrentContainerPlacements = containerPlacements;
            CurrentContainerHeaders = containerHeaders;
            DropTabs = NativeLayoutHeader.CaptureDropTabs();

            if (focusedContainerArea is LayoutArea focusedArea)
            {
                _focusedContainerBorder.SetBounds(focusedArea, parentFocusWindow);
            }
            else
            {
                ClearParentFocus();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Layout error: {exception}");
        }
        finally
        {
            _isApplyingLayout = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _focusedContainerBorder.Dispose();
        var noMonitors = new HashSet<nint>();
        NativeLayoutHeader.RemoveHeadersNotOnMonitors(noMonitors);
        NativeTopBar.RemoveBarsNotOnMonitors(noMonitors);
        _disposed = true;
    }
}
