using System.Diagnostics;
using Ortilo.App.Input;
using Ortilo.Core.Models;
using Ortilo.Core.Services;
using Ortilo.Platform.Windows.Input;
using Ortilo.Platform.Windows.Windows;

namespace Ortilo.App.Application;

public sealed class OrtiloApplication : IDisposable
{
    private readonly WindowRegistry _registry = new();
    private readonly LayoutManager _layoutManager = new();
    private readonly WorkspaceManager _workspaceManager = new();
    private readonly HotkeyManager _hotkeyManager = new();
    private readonly WindowEventListener _windowEventListener = new();
    private readonly NativeDisplayChangeListener _displayChangeListener = new();

    private OrtiloConfig _config = new();

    private LayoutCoordinator? _layoutCoordinator;
    private WindowNavigationController? _navigationController;
    private WindowStateController? _windowStateController;
    private WorkspaceController? _workspaceController;
    private WindowEventCoordinator? _windowEventCoordinator;
    private NativeTrayIcon? _trayIcon;

    private Dictionary<string, Action<int?>>? _hotkeyActions;

    private bool _initialized;
    private bool _disposed;

    public OrtiloApplication()
    {
        _displayChangeListener.DisplayConfigurationChanged +=
            OnDisplayConfigurationChanged;
        _displayChangeListener.WorkAreaChanged += OnWorkAreaChanged;
        _displayChangeListener.ReloadConfigRequested += ReloadConfig;
        _displayChangeListener.ResetRequested += Reset;
    }

    public void Run()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IReadOnlyList<HotkeyDefinition> bindings;
        try
        {
            var config = OrtiloConfig.Load(createIfMissing: true);
            bindings = config.Validate();
            _config = config;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Using defaults. Could not load {OrtiloConfig.FilePath}: {exception.Message}");
            bindings = DefaultHotkeys.Create();
        }

        Console.WriteLine($"Configuration: {OrtiloConfig.FilePath}");
        Initialize();
        _windowEventListener.Run(() =>
        {
            RegisterInitialHotkeys(bindings);
            try
            {
                _trayIcon = new NativeTrayIcon(GetTrayMenu);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not initialize the Ortilo tray icon. {exception.Message}");
            }
        });
    }

    private IReadOnlyList<TrayMenuItem> GetTrayMenu() =>
    [
        new("Open configuration", OpenConfig, TrayMenuIcon.Settings),
        new("Reload configuration", ReloadConfig, TrayMenuIcon.Refresh, TrayShortcut(HotkeyActions.ReloadConfig)),
        new("Reset Ortilo", Reset, TrayMenuIcon.Repair, TrayShortcut(HotkeyActions.Reset)),
        new("", null),
        new("Quit Ortilo", _windowEventListener.QuitMessageLoop, TrayMenuIcon.Power)
    ];

    private string? TrayShortcut(string action) =>
        _hotkeyManager.GetBinding(action) is { } binding ? OrtiloConfig.FormatHotkey(binding) : null;

    private static void OpenConfig()
    {
        string path = OrtiloConfig.EnsureExists();
        var editor = new ProcessStartInfo("notepad.exe");
        editor.ArgumentList.Add(path);
        using var process = Process.Start(editor);
    }

    private void RegisterInitialHotkeys(IReadOnlyList<HotkeyDefinition> bindings)
    {
        try
        {
            _hotkeyManager.RegisterAll(bindings);
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Console.Error.WriteLine($"{exception.Message} Trying default hotkeys.");
            try
            {
                _hotkeyManager.RegisterAll(DefaultHotkeys.Create());
            }
            catch (System.ComponentModel.Win32Exception fallback)
            {
                Console.Error.WriteLine($"{fallback.Message} Use the command-line reload command after editing the bindings.");
            }
        }
    }

    private void Initialize()
    {
        if (_initialized)
            return;

        _workspaceManager.ConfigureMonitors(
            MonitorEnumerator.GetMonitors(),
            _config.Monitors.ToOptions()
        );
        foreach (var workspace in _workspaceManager.Workspaces)
            workspace.Layout = _config.DefaultLayout;

        _layoutCoordinator = new LayoutCoordinator(
            _registry,
            _workspaceManager
        );
        _layoutCoordinator.Configure(_config);

        _workspaceController = new WorkspaceController(
            _registry,
            _workspaceManager,
            _layoutManager,
            _layoutCoordinator
        );

        _navigationController = new WindowNavigationController(
            _workspaceManager,
            _layoutCoordinator,
            _workspaceController
        );
        _navigationController.ResizeStep = _config.ResizeStep;

        _windowStateController = new WindowStateController(
            _workspaceManager,
            ApplyLayout
        );

        RegisterNativeEvents();

        SynchronizeWindows();

        Console.WriteLine("Ortilo - Window Classification");
        Console.WriteLine("==============================");

        PrintSummary();
        ApplyLayout();

        RegisterHotkeyActions();

        _windowEventCoordinator = new WindowEventCoordinator(
            _registry,
            _workspaceManager,
            _layoutCoordinator,
            PrintSummary,
            _workspaceController,
            _windowEventListener
        );
        _windowEventCoordinator.ConfigureBorder(_config.FocusedWindowBorder);
        _windowEventCoordinator.LiveDragPreview = _config.LiveDragPreview;

        _workspaceController.FocusSelectedWindow();

        _initialized = true;
    }

    private void RegisterNativeEvents()
    {
        NativeLayoutHeader.WindowSelected += OnWindowSelected;
        NativeTopBar.WorkspaceSelected += OnWorkspaceSelected;
        NativeTopBar.LayoutInvalidated += ApplyLayout;

        _windowEventListener.HotkeyPressed += OnHotkeyPressed;
    }

    private void OnLayoutRefreshRequested()
    {
        if (_disposed || !_initialized)
            return;

        ApplyLayout();
    }

    private void SynchronizeWindows()
    {
        _registry.Synchronize(
            WindowEnumerator.GetTopLevelWindows()
        );

        nint previouslyActiveMonitor =
            _workspaceManager.ActiveMonitorHandle;

        try
        {
            foreach (var window in _registry.TiledWindows)
            {
                if (_workspaceManager.GetWorkspaceForWindow(window.Handle) is not null)
                    continue;

                nint monitorHandle =
                    MonitorEnumerator.GetMonitorForWindow(window.Handle);

                if (!_workspaceManager.SetActiveMonitor(monitorHandle))
                    continue;

                _workspaceManager.AddWindow(window.Handle);
            }
        }
        finally
        {
            if (previouslyActiveMonitor != nint.Zero)
            {
                _workspaceManager.SetActiveMonitor(
                    previouslyActiveMonitor
                );
            }
        }
    }

    private void OnDisplayConfigurationChanged()
    {
        if (_disposed || !_initialized)
            return;

        try
        {
            var monitors = MonitorEnumerator.GetMonitors();

            _workspaceManager.ConfigureMonitors(
                monitors,
                _config.Monitors.ToOptions()
            );

            SynchronizeWindows();
            _workspaceController?.RefreshWindowVisibility();

            ApplyLayout();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Could not refresh display configuration: {exception}"
            );
        }
    }

    private void OnWorkAreaChanged()
    {
        if (_disposed || !_initialized)
            return;

        try
        {
            if (_workspaceManager.UpdateMonitorWorkAreas(MonitorEnumerator.GetMonitors()))
                ApplyLayout();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not refresh monitor work areas: {exception}");
        }
    }

    private void ReloadConfig()
    {
        if (_disposed || !_initialized)
            return;

        try
        {
            var config = OrtiloConfig.Load(createIfMissing: false);
            var bindings = config.Validate();
            var monitors = MonitorEnumerator.GetMonitors();
            _hotkeyManager.ReplaceAll(bindings);

            _config = config;
            _workspaceManager.ConfigureMonitors(monitors, config.Monitors.ToOptions());
            _layoutCoordinator!.Configure(config);
            _navigationController!.ResizeStep = config.ResizeStep;
            _windowEventCoordinator!.LiveDragPreview = config.LiveDragPreview;
            RefreshManagedWindows();
            _windowEventCoordinator.ConfigureBorder(config.FocusedWindowBorder);
            Console.WriteLine($"Reloaded {OrtiloConfig.FilePath}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not reload configuration: {exception.Message}");
        }
    }

    private void Reset()
    {
        if (_disposed || !_initialized)
            return;

        try
        {
            _workspaceManager.ConfigureMonitors(MonitorEnumerator.GetMonitors(), _config.Monitors.ToOptions());
            _layoutCoordinator!.Configure(_config);
            RefreshManagedWindows();
            _windowEventCoordinator!.ConfigureBorder(_config.FocusedWindowBorder);
            Console.WriteLine("Refreshed displays, managed windows, and layouts.");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not refresh Ortilo: {exception.Message}");
        }
    }

    private void RefreshManagedWindows()
    {
        var closedHandles = _workspaceManager.Workspaces.SelectMany(workspace => workspace.WindowHandles)
            .Where(handle => !WindowVisibility.Exists(handle)).ToArray();
        foreach (nint handle in closedHandles)
            _workspaceManager.RemoveWindow(handle);

        SynchronizeWindows();
        _workspaceController!.RefreshWindowVisibility();
        nint foreground = WindowFocuser.GetForegroundHandle();
        if (_workspaceManager.GetWorkspaceForWindow(foreground) is { } owner &&
            _workspaceManager.GetMonitorForWorkspace(owner.Number) is nint monitor)
        {
            _workspaceManager.SetActiveMonitor(monitor);
        }
        ApplyLayout();
    }

    private void RegisterHotkeyActions()
    {
        _hotkeyActions = new Dictionary<string, Action<int?>>(
            StringComparer.Ordinal)
        {
            [HotkeyActions.Reapply] =
                _ => ApplyLayout(),
            [HotkeyActions.ReloadConfig] = _ => ReloadConfig(),
            [HotkeyActions.Reset] = _ => Reset(),

            [HotkeyActions.FocusLeft] =
                _ => _navigationController!.FocusWindow(FocusDirection.Left),

            [HotkeyActions.FocusRight] =
                _ => _navigationController!.FocusWindow(FocusDirection.Right),

            [HotkeyActions.FocusUp] =
                _ => _navigationController!.FocusWindow(FocusDirection.Up),

            [HotkeyActions.FocusDown] =
                _ => _navigationController!.FocusWindow(FocusDirection.Down),

            [HotkeyActions.MoveLeft] =
                _ => _navigationController!.MoveWindow(FocusDirection.Left),

            [HotkeyActions.MoveRight] =
                _ => _navigationController!.MoveWindow(FocusDirection.Right),

            [HotkeyActions.MoveUp] =
                _ => _navigationController!.MoveWindow(FocusDirection.Up),

            [HotkeyActions.MoveDown] =
                _ => _navigationController!.MoveWindow(FocusDirection.Down),

            [HotkeyActions.SwitchWorkspace] =
                number =>
                {
                    if (number is int workspaceNumber)
                        _workspaceController!.SwitchTo(workspaceNumber);
                },

            [HotkeyActions.MoveToWorkspace] =
                number =>
                {
                    if (number is int workspaceNumber)
                        _workspaceController!.MoveFocusedWindowTo(workspaceNumber);
                },
            [HotkeyActions.MoveWorkspaceToNextMonitor] =
                _ => MoveCurrentWorkspaceToAdjacentMonitor(1),

            [HotkeyActions.MoveWorkspaceToPreviousMonitor] =
                _ => MoveCurrentWorkspaceToAdjacentMonitor(-1),

            [HotkeyActions.MasterStack] =
                _ => SetLayout(LayoutType.MasterStack),

            [HotkeyActions.Stacking] =
                _ => SetLayout(LayoutType.Stacking),

            [HotkeyActions.Tabbed] =
                _ => SetLayout(LayoutType.Tabbed),

            [HotkeyActions.ContainerTree] =
                _ => SetLayout(LayoutType.ContainerTree),

            [HotkeyActions.ResizeLeft] =
                _ => _navigationController!.ResizeWindow(FocusDirection.Left),

            [HotkeyActions.ResizeRight] =
                _ => _navigationController!.ResizeWindow(FocusDirection.Right),

            [HotkeyActions.ResizeUp] =
                _ => _navigationController!.ResizeWindow(FocusDirection.Up),

            [HotkeyActions.ResizeDown] =
                _ => _navigationController!.ResizeWindow(FocusDirection.Down),

            [HotkeyActions.FocusParent] =
                _ => _navigationController!.FocusParent(),

            [HotkeyActions.FocusChild] =
                _ => _navigationController!.FocusChild(),

            [HotkeyActions.ToggleSplit] =
                _ => _navigationController!.ToggleFocusedSplit(),

            [HotkeyActions.Fullscreen] =
                _ => _windowStateController!.ToggleFullscreen(),

            [HotkeyActions.Minimize] =
                _ => _windowStateController!.MinimizeFocusedWindow(),

            [HotkeyActions.ToggleFloating] =
                _ => _windowStateController!.ToggleFloating()
        };
    }

    private void MoveCurrentWorkspaceToAdjacentMonitor(int direction)
    {
        var monitors = _workspaceManager.ManagedMonitors;

        if (monitors.Count < 2)
            return;

        nint activeMonitorHandle =
            _workspaceManager.ActiveMonitorHandle;

        int activeIndex = -1;

        for (int i = 0; i < monitors.Count; i++)
        {
            if (monitors[i].Handle == activeMonitorHandle)
            {
                activeIndex = i;
                break;
            }
        }

        if (activeIndex < 0)
            return;

        int targetIndex =
            (activeIndex + direction + monitors.Count) % monitors.Count;

        int workspaceNumber =
            _workspaceManager.CurrentWorkspace.Number;

        _workspaceController?.MoveWorkspaceToMonitor(
            workspaceNumber,
            monitors[targetIndex].Handle
        );
    }

    private void OnHotkeyPressed(int hotkeyId)
    {
        if (_hotkeyActions is null)
            return;

        var definition = _hotkeyManager.GetDefinition(hotkeyId);

        if (definition is null)
            return;

        if (!_hotkeyActions.TryGetValue(
            definition.ActionName,
            out var action))
        {
            return;
        }

        action(definition.Argument);
    }

    private void OnWindowSelected(nint handle)
    {
        if (_workspaceManager.GetWorkspaceForWindow(handle) is not { } workspace ||
            _workspaceManager.GetMonitorForWorkspace(workspace.Number) is not nint monitorHandle ||
            !WindowFocuser.TryFocus(handle))
            return;

        _workspaceManager.SetActiveMonitor(monitorHandle);
        _workspaceManager.FocusWindow(handle);
        ApplyLayout();
    }

    private void OnWorkspaceSelected(nint monitorHandle, int number)
    {
        _workspaceController?.SelectOnMonitor(monitorHandle, number);
    }

    private void SetLayout(LayoutType layout)
    {
        var workspace = _workspaceManager.CurrentWorkspace;
        if (workspace.Layout == LayoutType.ContainerTree)
        {
            if (layout == LayoutType.ContainerTree)
            {
                if (workspace.LayoutTree.RestoreFocusedGroupSplit())
                    ApplyLayout();
                return;
            }

            if (layout is LayoutType.Tabbed or LayoutType.Stacking)
            {
                if (workspace.LayoutTree.SetFocusedGroupLayout(layout))
                    ApplyLayout();
                return;
            }
        }

        _layoutManager.SetLayout(layout);
        workspace.Layout = layout;

        Console.WriteLine();
        Console.WriteLine($"Layout changed to: {layout}");

        ApplyLayout();
    }

    private void ApplyLayout()
    {
        _layoutCoordinator?.ApplyLayout();
    }

    private void PrintSummary()
    {
        Console.WriteLine(
            $"Tiled: {_registry.TiledWindows.Count} | " +
            $"Floating: {_registry.FloatingWindows.Count} | " +
            $"Ignored: {_registry.IgnoredWindows.Count}"
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Restore visibility before tearing down native resources. A failure
        // disposing a tray icon, header, or backdrop must not strand windows.
        WindowVisibility.RestoreHiddenWindows();

        _displayChangeListener.DisplayConfigurationChanged -=
            OnDisplayConfigurationChanged;
        _displayChangeListener.WorkAreaChanged -= OnWorkAreaChanged;
        _displayChangeListener.ReloadConfigRequested -= ReloadConfig;
        _displayChangeListener.ResetRequested -= Reset;
        _windowEventListener.HotkeyPressed -= OnHotkeyPressed;

        NativeLayoutHeader.WindowSelected -= OnWindowSelected;
        NativeTopBar.WorkspaceSelected -= OnWorkspaceSelected;
        NativeTopBar.LayoutInvalidated -= ApplyLayout;

        _trayIcon?.Dispose();
        _windowEventCoordinator?.Dispose();
        _layoutCoordinator?.Dispose();
        foreach (nint handle in _workspaceManager.Workspaces.SelectMany(workspace => workspace.WindowHandles))
        {
            if (WindowVisibility.IsMinimized(handle))
                continue;

            if (FullscreenManager.IsFullscreen(handle))
                FullscreenManager.Toggle(handle);
        }
        _displayChangeListener.Dispose();
        _hotkeyManager.Dispose();
        NativeTopBar.ShutDownComposition();
    }
}
