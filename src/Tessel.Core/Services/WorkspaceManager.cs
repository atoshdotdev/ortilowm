using Tessel.Core.Models;

namespace Tessel.Core.Services;

public sealed class WorkspaceManager
{
    private sealed record TemporaryWorkspaceAssignment(string OriginalMonitorDeviceName, int WorkspaceNumber);

    private readonly Dictionary<int, Workspace> _workspaces = [];
    private readonly Dictionary<string, int> _monitorWorkspaces =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _workspaceMonitorPreferences = [];
    private readonly Dictionary<int, string> _configuredWorkspaceMonitors = [];
    private readonly Dictionary<string, TemporaryWorkspaceAssignment>
    _temporaryMonitorWorkspaces =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<DisplayMonitor> _managedMonitors = [];

    private string? _activeMonitorDeviceName;

    public WorkspaceManager(int workspaceCount = 9)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workspaceCount);

        for (int i = 1; i <= workspaceCount; i++)
            _workspaces[i] = new Workspace(i);
    }

    public Workspace CurrentWorkspace
    {
        get
        {
            if (_activeMonitorDeviceName is not null &&
                GetAssignedWorkspaceNumber(_activeMonitorDeviceName)
                    is int number)
            {
                return _workspaces[number];
            }

            return _workspaces[1];
        }
    }

    public IReadOnlyCollection<Workspace> Workspaces =>
        _workspaces.Values;

    public IReadOnlyList<DisplayMonitor> ManagedMonitors =>
        _managedMonitors;

    public bool UpdateMonitorWorkAreas(IReadOnlyList<DisplayMonitor> monitors)
    {
        DisplayMonitor[]? updated = null;
        for (int index = 0; index < _managedMonitors.Count; index++)
        {
            var current = _managedMonitors[index];
            var latest = monitors.FirstOrDefault(monitor => monitor.Handle == current.Handle &&
                monitor.Bounds == current.Bounds && monitor.IsPrimary == current.IsPrimary &&
                string.Equals(monitor.DeviceName, current.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (latest is null || latest.WorkArea == current.WorkArea)
                continue;

            updated ??= _managedMonitors.ToArray();
            updated[index] = current with { WorkArea = latest.WorkArea };
        }

        if (updated is null)
            return false;

        _managedMonitors = updated;
        return true;
    }

    public nint ActiveMonitorHandle =>
        _managedMonitors.FirstOrDefault(
            monitor => string.Equals(
                monitor.DeviceName,
                _activeMonitorDeviceName,
                StringComparison.OrdinalIgnoreCase
            )
        )?.Handle ?? nint.Zero;

    public void ConfigureMonitors(
        IReadOnlyList<DisplayMonitor> monitors,
        MonitorManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.SelectedDeviceNames);
        ArgumentNullException.ThrowIfNull(options.WorkspaceDeviceNames);

        if (!Enum.IsDefined(options.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.Mode),
                options.Mode,
                "Unknown monitor management mode.");
        }

        ValidateMonitors(monitors);
        if (options.WorkspaceDeviceNames.Any(pair =>
                !_workspaces.ContainsKey(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)))
            throw new ArgumentException("Invalid configured workspace assignment.", nameof(options));

        var selectedNames = options.SelectedDeviceNames
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var eligible = monitors
            .Where(monitor => options.Mode switch
            {
                MonitorManagementMode.All => true,
                MonitorManagementMode.PrimaryOnly => monitor.IsPrimary,
                MonitorManagementMode.Selected =>
                    selectedNames.Contains(monitor.DeviceName),
                _ => false
            })
            .OrderByDescending(monitor => monitor.IsPrimary)
            .ThenBy(
                monitor => monitor.DeviceName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var previousManagedNames = _managedMonitors
            .Select(monitor => monitor.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var currentManagedNames = eligible
            .Select(monitor => monitor.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string? previouslyActiveMonitorName = _activeMonitorDeviceName;

        int? previouslyActiveWorkspaceNumber =
            previouslyActiveMonitorName is not null
                ? GetAssignedWorkspaceNumber(previouslyActiveMonitorName)
                : null;

        var previouslyKnownMonitorNames = _monitorWorkspaces.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var reconnectedNames = currentManagedNames
            .Where(name =>
                previouslyKnownMonitorNames.Contains(name) &&
                !previousManagedNames.Contains(name))
            .ToArray();

        foreach (var reconnectedName in reconnectedNames)
        {
            var temporaryAssignmentsToRemove = _temporaryMonitorWorkspaces
                .Where(pair => string.Equals(
                    pair.Value.OriginalMonitorDeviceName,
                    reconnectedName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var deviceName in temporaryAssignmentsToRemove)
                _temporaryMonitorWorkspaces.Remove(deviceName);
        }

        ApplyConfiguredAssignments(eligible, options.WorkspaceDeviceNames);
        var assignedWorkspaces = _monitorWorkspaces.Values.ToHashSet();

        foreach (var monitor in eligible)
        {
            if (_monitorWorkspaces.ContainsKey(monitor.DeviceName))
                continue;

            int? availableWorkspace = _workspaces.Keys
                .Where(number =>
                    !assignedWorkspaces.Contains(number) &&
                    (GetPreferredMonitor(number) is not string preferredMonitor ||
                     string.Equals(
                        preferredMonitor,
                        monitor.DeviceName,
                        StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(number => _configuredWorkspaceMonitors.ContainsKey(number))
                .Cast<int?>()
                .FirstOrDefault();

            if (availableWorkspace is null)
                break;

            _monitorWorkspaces[monitor.DeviceName] =
                availableWorkspace.Value;

            _workspaceMonitorPreferences.TryAdd(
                availableWorkspace.Value,
                monitor.DeviceName);

            assignedWorkspaces.Add(availableWorkspace.Value);
        }

        var disconnectedNames = previousManagedNames
            .Where(name => !currentManagedNames.Contains(name))
            .ToList();

        if (previouslyActiveMonitorName is not null &&
            previouslyActiveWorkspaceNumber is not null &&
            disconnectedNames.Remove(previouslyActiveMonitorName))
        {
            disconnectedNames.Insert(0, previouslyActiveMonitorName);
        }

        foreach (var disconnectedName in disconnectedNames)
        {
            int? disconnectedWorkspaceNumber =
                string.Equals(
                    disconnectedName,
                    previouslyActiveMonitorName,
                    StringComparison.OrdinalIgnoreCase)
                    ? previouslyActiveWorkspaceNumber
                    : _monitorWorkspaces.TryGetValue(
                        disconnectedName,
                        out int permanentNumber)
                            ? permanentNumber
                            : null;

            if (disconnectedWorkspaceNumber is null)
                continue;

            var targetMonitor = eligible.FirstOrDefault(monitor =>
                !_temporaryMonitorWorkspaces.ContainsKey(monitor.DeviceName));

            if (targetMonitor is null)
                continue;

            _temporaryMonitorWorkspaces[targetMonitor.DeviceName] = new TemporaryWorkspaceAssignment(
                disconnectedName,
                disconnectedWorkspaceNumber.Value
            );
        }

        _managedMonitors = eligible
            .Where(monitor =>
                _monitorWorkspaces.ContainsKey(monitor.DeviceName))
            .ToArray();

        bool activeMonitorAvailable =
            _activeMonitorDeviceName is not null &&
            _managedMonitors.Any(monitor =>
                string.Equals(
                    monitor.DeviceName,
                    _activeMonitorDeviceName,
                    StringComparison.OrdinalIgnoreCase));

        if (!activeMonitorAvailable)
        {
            _activeMonitorDeviceName =
                _managedMonitors
                    .FirstOrDefault(monitor => monitor.IsPrimary)
                    ?.DeviceName
                ?? _managedMonitors.FirstOrDefault()?.DeviceName;
        }
    }

    private void ApplyConfiguredAssignments(IReadOnlyList<DisplayMonitor> monitors,
        IReadOnlyDictionary<int, string> assignments)
    {
        if (assignments.Count == _configuredWorkspaceMonitors.Count &&
            assignments.All(pair => string.Equals(_configuredWorkspaceMonitors.GetValueOrDefault(pair.Key),
                pair.Value, StringComparison.OrdinalIgnoreCase)))
            return;

        _configuredWorkspaceMonitors.Clear();
        foreach (var (workspace, device) in assignments)
            _configuredWorkspaceMonitors.Add(workspace, device);

        // Keep a matching visible workspace. Otherwise display the first configured one.
        foreach (var monitor in monitors)
        {
            string device = monitor.DeviceName;
            var choices = assignments.Where(pair =>
                string.Equals(pair.Value, device, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key).Order().ToArray();
            if (choices.Length == 0)
                continue;

            int workspace = GetAssignedWorkspaceNumber(device) is int current && choices.Contains(current)
                ? current : choices[0];
            foreach (string oldDevice in _monitorWorkspaces
                .Where(pair => pair.Value == workspace && !string.Equals(pair.Key, device,
                    StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray())
            {
                _monitorWorkspaces.Remove(oldDevice);
            }

            foreach (string temporaryDevice in _temporaryMonitorWorkspaces
                .Where(pair => pair.Value.WorkspaceNumber == workspace ||
                    string.Equals(pair.Key, device, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key).ToArray())
            {
                _temporaryMonitorWorkspaces.Remove(temporaryDevice);
            }

            _monitorWorkspaces[device] = workspace;
        }
    }

    private string? GetPreferredMonitor(int number) =>
        _configuredWorkspaceMonitors.GetValueOrDefault(number) ??
        _workspaceMonitorPreferences.GetValueOrDefault(number);

    private static void ValidateMonitors(
        IReadOnlyList<DisplayMonitor> monitors)
    {
        var deviceNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var handles = new HashSet<nint>();

        foreach (var monitor in monitors)
        {
            ArgumentNullException.ThrowIfNull(monitor);

            if (string.IsNullOrWhiteSpace(monitor.DeviceName))
            {
                throw new ArgumentException(
                    "Every monitor must have a device name.",
                    nameof(monitors));
            }

            if (monitor.Handle == nint.Zero)
            {
                throw new ArgumentException(
                    "Every monitor must have a valid handle.",
                    nameof(monitors));
            }

            if (!deviceNames.Add(monitor.DeviceName))
            {
                throw new ArgumentException(
                    $"Duplicate monitor device name: {monitor.DeviceName}.",
                    nameof(monitors));
            }

            if (!handles.Add(monitor.Handle))
            {
                throw new ArgumentException(
                    $"Duplicate monitor handle: {monitor.Handle}.",
                    nameof(monitors));
            }
        }
    }

    public bool SetActiveMonitor(nint monitorHandle)
    {
        var monitor = _managedMonitors.FirstOrDefault(
            item => item.Handle == monitorHandle);

        if (monitor is null)
            return false;

        _activeMonitorDeviceName = monitor.DeviceName;
        return true;
    }

    public bool IsManagedMonitor(nint monitorHandle)
    {
        return _managedMonitors.Any(
            monitor => monitor.Handle == monitorHandle);
    }

    public Workspace? GetWorkspaceForMonitor(nint monitorHandle)
    {
        var monitor = _managedMonitors.FirstOrDefault(
            item => item.Handle == monitorHandle);

        if (monitor is null)
            return null;

        int? number = GetAssignedWorkspaceNumber(monitor.DeviceName);

        return number is not null
            ? _workspaces[number.Value]
            : null;
    }

    public nint? GetMonitorForWorkspace(int workspaceNumber)
    {
        foreach (var monitor in _managedMonitors)
        {
            if (GetAssignedWorkspaceNumber(monitor.DeviceName)
                == workspaceNumber)
            {
                return monitor.Handle;
            }
        }

        return null;
    }

    public bool SwitchTo(int number)
    {
        if (!_workspaces.ContainsKey(number) ||
            _activeMonitorDeviceName is null ||
            !_monitorWorkspaces.ContainsKey(_activeMonitorDeviceName))
        {
            return false;
        }

        int? currentNumber =
            GetAssignedWorkspaceNumber(_activeMonitorDeviceName);

        if (currentNumber == number)
            return true;

        nint? visibleMonitor = GetMonitorForWorkspace(number);

        if (visibleMonitor is nint visibleMonitorHandle)
            return SetActiveMonitor(visibleMonitorHandle);

        if (GetPreferredMonitor(number) is string preferredMonitorName)
        {
            bool preferredMonitorConnected = _managedMonitors.Any(monitor =>
                string.Equals(
                    monitor.DeviceName,
                    preferredMonitorName,
                    StringComparison.OrdinalIgnoreCase));

            if (preferredMonitorConnected)
            {
                _temporaryMonitorWorkspaces.Remove(preferredMonitorName);

                _monitorWorkspaces[preferredMonitorName] = number;
                _activeMonitorDeviceName = preferredMonitorName;

                return true;
            }

            if (_temporaryMonitorWorkspaces.TryGetValue(
                _activeMonitorDeviceName,
                out var existingTemporaryAssignment))
            {
                _temporaryMonitorWorkspaces[_activeMonitorDeviceName] =
                    existingTemporaryAssignment with
                    {
                        WorkspaceNumber = number
                    };

                return true;
            }

            _temporaryMonitorWorkspaces[_activeMonitorDeviceName] =
                new TemporaryWorkspaceAssignment(
                    preferredMonitorName,
                    number);

            return true;
        }

        if (_temporaryMonitorWorkspaces.TryGetValue(
            _activeMonitorDeviceName,
            out var temporaryAssignment))
        {
            _temporaryMonitorWorkspaces[_activeMonitorDeviceName] =
                temporaryAssignment with
                {
                    WorkspaceNumber = number
                };

            return true;
        }

        _workspaceMonitorPreferences[number] =
            _activeMonitorDeviceName;

        _monitorWorkspaces[_activeMonitorDeviceName] = number;

        return true;
    }

    public bool MoveWorkspaceToMonitor(int workspaceNumber, nint monitorHandle)
    {
        if (!_workspaces.ContainsKey(workspaceNumber))
            return false;

        var targetMonitor = _managedMonitors.FirstOrDefault(
            monitor => monitor.Handle == monitorHandle);

        if (targetMonitor is null)
            return false;

        string targetDeviceName = targetMonitor.DeviceName;

        if (GetAssignedWorkspaceNumber(targetDeviceName) == workspaceNumber)
        {
            _workspaceMonitorPreferences[workspaceNumber] = targetDeviceName;
            return true;
        }

        var sourceMonitor = _managedMonitors.FirstOrDefault(
            monitor =>
                GetAssignedWorkspaceNumber(monitor.DeviceName) == workspaceNumber);

        if (sourceMonitor is null)
        {
            _workspaceMonitorPreferences[workspaceNumber] = targetDeviceName;
            return true;
        }

        string sourceDeviceName = sourceMonitor.DeviceName;

        if (string.Equals(
            sourceDeviceName,
            targetDeviceName,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool sourceIsTemporary =
            _temporaryMonitorWorkspaces.TryGetValue(
                sourceDeviceName,
                out var sourceTemporaryAssignment);

        int? displacedWorkspaceNumber =
            GetAssignedWorkspaceNumber(targetDeviceName);

        _temporaryMonitorWorkspaces.Remove(sourceDeviceName);
        _temporaryMonitorWorkspaces.Remove(targetDeviceName);

        _monitorWorkspaces[targetDeviceName] = workspaceNumber;
        _workspaceMonitorPreferences[workspaceNumber] = targetDeviceName;

        if (sourceIsTemporary)
        {
            string originalMonitorName =
                sourceTemporaryAssignment!.OriginalMonitorDeviceName;

            if (_monitorWorkspaces.TryGetValue(
                    originalMonitorName,
                    out int reservedWorkspaceNumber) &&
                reservedWorkspaceNumber == workspaceNumber)
            {
                _monitorWorkspaces.Remove(originalMonitorName);
            }
        }
        else if (displacedWorkspaceNumber is int displacedNumber)
        {
            _monitorWorkspaces[sourceDeviceName] = displacedNumber;
            _workspaceMonitorPreferences[displacedNumber] = sourceDeviceName;
        }

        return true;
    }

    private int? GetAssignedWorkspaceNumber(string deviceName)
    {
        if (_temporaryMonitorWorkspaces.TryGetValue(
                deviceName,
                out var temporaryAssignment))
        {
            return temporaryAssignment.WorkspaceNumber;
        }

        if (_monitorWorkspaces.TryGetValue(
                deviceName,
                out int permanentNumber))
        {
            return permanentNumber;
        }

        return null;
    }

    public Workspace? GetAdjacentWorkspace(FocusDirection direction)
    {
        var monitorBounds = _managedMonitors
            .Select(monitor => new WindowPlacement(
                monitor.Handle, monitor.Bounds.X, monitor.Bounds.Y,
                monitor.Bounds.Width, monitor.Bounds.Height))
            .ToArray();

        nint? targetMonitor = FocusNavigator.FindNext(monitorBounds, ActiveMonitorHandle, direction);
        return targetMonitor is nint handle ? GetWorkspaceForMonitor(handle) : null;
    }

    public bool MoveWindowToAdjacentWorkspace(nint handle, FocusDirection direction)
    {
        if (!CurrentWorkspace.WindowHandles.Contains(handle) ||
            GetAdjacentWorkspace(direction) is not { } target ||
            !MoveWindowToWorkspace(handle, target.Number))
        {
            return false;
        }

        SwitchTo(target.Number);
        FocusWindow(handle);
        return true;
    }

    public bool MoveFloatingWindowToMonitor(nint handle, nint monitorHandle)
    {
        var source = GetWorkspaceForWindow(handle);
        var destination = GetWorkspaceForMonitor(monitorHandle);

        return source is not null && source.FloatingWindowHandles.Contains(handle) &&
            destination is not null && destination != source &&
            MoveWindowToWorkspace(handle, destination.Number);
    }

    public bool ReleaseWindowToUnmanagedMonitor(nint handle, nint monitorHandle)
    {
        if (monitorHandle == nint.Zero || IsManagedMonitor(monitorHandle) ||
            GetWorkspaceForWindow(handle) is null)
            return false;

        RemoveWindow(handle);
        return true;
    }

    public bool MoveWindowToWorkspace(nint handle, int number)
    {
        if (!_workspaces.TryGetValue(number, out var target) ||
            GetWorkspaceForWindow(handle) is not { } source)
            return false;

        return TransferWindow(source, target, handle);
    }

    public static bool TransferWindow(Workspace source, Workspace target, nint handle)
    {
        if (source.Number == target.Number || !source.WindowHandles.Remove(handle))
            return false;

        bool wasFloating = source.FloatingWindowHandles.Remove(handle);
        source.LayoutTree.RemoveWindow(handle);

        if (source.SelectedWindowHandle == handle)
        {
            nint next = source.LayoutTree.FocusedWindowHandle ?? source.WindowHandles.FirstOrDefault();
            source.SelectedWindowHandle = next != nint.Zero ? next : null;
        }

        target.WindowHandles.Add(handle);

        if (wasFloating)
        {
            target.FloatingWindowHandles.Add(handle);
        }
        else
        {
            target.LayoutTree.AddWindow(handle);
        }

        target.SelectedWindowHandle ??= handle;
        SynchronizeTreeFocus(target);

        return true;
    }

    public void AddWindow(nint handle)
    {
        var workspace = CurrentWorkspace;

        if (workspace.WindowHandles.Contains(handle))
            return;

        var selectedBefore = workspace.SelectedWindowHandle;
        var focusedNodeBefore = workspace.LayoutTree.FocusedNode;

        bool preserveContainerFocus =
            focusedNodeBefore is { IsLeaf: false } ||
            focusedNodeBefore?.PendingSplitOrientation is not null;

        workspace.WindowHandles.Add(handle);

        if (!workspace.FloatingWindowHandles.Contains(handle))
        {
            workspace.LayoutTree.AddWindow(handle);

            Console.WriteLine($"WINDOW ADDED: 0x{handle.ToInt64():X}");
            Console.WriteLine(
                $"ROOT ORIENTATION: {workspace.LayoutTree.Root.Orientation}");
            Console.WriteLine(
                $"ROOT IS SPLIT: {workspace.LayoutTree.Root.IsSplit}");

            workspace.LayoutTree.PrintDebugTree();
        }

        workspace.SelectedWindowHandle ??= handle;

        if (!preserveContainerFocus &&
            selectedBefore is nint selected &&
            workspace.LayoutTree.Contains(selected))
        {
            workspace.LayoutTree.FocusWindow(selected);
        }
    }

    public void RemoveWindow(nint handle)
    {
        foreach (var workspace in _workspaces.Values)
        {
            bool wasSelected =
                workspace.SelectedWindowHandle == handle;

            workspace.LayoutTree.RemoveWindow(handle);
            workspace.WindowHandles.Remove(handle);
            workspace.FloatingWindowHandles.Remove(handle);

            if (wasSelected)
            {
                nint next = workspace.LayoutTree.FocusedWindowHandle ??
                    workspace.WindowHandles.FirstOrDefault();

                workspace.SelectedWindowHandle =
                    next != nint.Zero ? next : null;
            }
            else if (workspace.SelectedWindowHandle is nint selected &&
                     !workspace.WindowHandles.Contains(selected))
            {
                workspace.SelectedWindowHandle = null;
                SynchronizeTreeFocus(workspace);
            }
        }
    }

    public bool SelectWindow(nint handle)
    {
        var workspace = GetWorkspaceForWindow(handle);

        if (workspace is null)
            return false;

        workspace.SelectedWindowHandle = handle;

        if (!workspace.FloatingWindowHandles.Contains(handle) &&
            workspace.LayoutTree.Contains(handle))
        {
            workspace.LayoutTree.SelectWindow(handle);
        }

        return true;
    }

    public bool FocusWindow(nint handle)
    {
        var workspace = GetWorkspaceForWindow(handle);

        if (workspace is null)
            return false;

        workspace.SelectedWindowHandle = handle;

        if (!workspace.FloatingWindowHandles.Contains(handle) &&
            workspace.LayoutTree.Contains(handle))
        {
            workspace.LayoutTree.FocusWindow(handle);
        }

        return true;
    }

    public bool SelectWindowPreservingContainerFocus(nint handle)
    {
        var workspace = GetWorkspaceForWindow(handle);

        if (workspace is null)
            return false;

        workspace.SelectedWindowHandle = handle;

        if (!workspace.FloatingWindowHandles.Contains(handle) &&
            workspace.LayoutTree.Contains(handle))
        {
            workspace.LayoutTree.SelectWindow(handle);
        }

        return true;
    }

    public bool ToggleFloating(nint handle)
    {
        var workspace = CurrentWorkspace;

        if (!workspace.WindowHandles.Contains(handle))
            return false;

        if (workspace.FloatingWindowHandles.Contains(handle))
        {
            workspace.FloatingWindowHandles.Remove(handle);
            workspace.LayoutTree.AddWindow(handle);
        }
        else
        {
            workspace.LayoutTree.RemoveWindow(handle);
            workspace.FloatingWindowHandles.Add(handle);
        }

        SynchronizeTreeFocus(workspace);

        return true;
    }

    public bool IsWindowVisible(nint handle)
    {
        var workspace = GetWorkspaceForWindow(handle);

        if (workspace is null)
            return false;

        return GetMonitorForWorkspace(workspace.Number) is not null;
    }

    public Workspace? GetWorkspaceForWindow(nint handle)
    {
        return _workspaces.Values.FirstOrDefault(
            workspace => workspace.WindowHandles.Contains(handle));
    }

    private static void SynchronizeTreeFocus(Workspace workspace)
    {
        if (workspace.SelectedWindowHandle is nint selected &&
            workspace.LayoutTree.Contains(selected))
        {
            workspace.LayoutTree.FocusWindow(selected);
        }
    }
}
