using Tessel.Core.Models;
using Tessel.Core.Services;

namespace Tessel.Core.Tests;

public class WorkspaceManagerTests
{
    private static DisplayMonitor CreateMonitor(
        nint handle,
        string deviceName,
        bool isPrimary,
        int x = 0)
    {
        var area = new LayoutArea(x, 0, 1920, 1080);

        return new DisplayMonitor(
            handle,
            deviceName,
            area,
            area,
            isPrimary
        );
    }

    private static WorkspaceManager CreateManager()
    {
        var manager = new WorkspaceManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
                CreateMonitor(2, @"\\.\DISPLAY2", false, x: 1920)
            ],
            new MonitorManagementOptions()
        );

        return manager;
    }

    [Fact]
    public void UpdateMonitorWorkAreas_ChangesTaskbarReservationAndKeepsWorkspaceAssignments()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        Assert.True(manager.SwitchTo(2));
        manager.AddWindow(20);
        var originals = manager.ManagedMonitors.ToArray();
        var primary = originals.Single(monitor => monitor.IsPrimary);
        var secondary = originals.Single(monitor => !monitor.IsPrimary);
        var reserved = primary with { WorkArea = primary.Bounds with { Height = primary.Bounds.Height - 48 } };

        Assert.True(manager.UpdateMonitorWorkAreas([reserved, secondary]));
        Assert.Equal(reserved.WorkArea, manager.ManagedMonitors.Single(monitor => monitor.IsPrimary).WorkArea);
        Assert.Equal(secondary, manager.ManagedMonitors.Single(monitor => !monitor.IsPrimary));
        Assert.False(manager.UpdateMonitorWorkAreas([reserved, secondary]));
        Assert.True(manager.UpdateMonitorWorkAreas(originals));

        Assert.Equal(primary.Bounds, manager.ManagedMonitors.Single(monitor => monitor.IsPrimary).WorkArea);
        Assert.Equal((nint)2, manager.ActiveMonitorHandle);
        Assert.Same(manager.GetWorkspaceForMonitor(1), manager.GetWorkspaceForWindow(10));
        Assert.Same(manager.GetWorkspaceForMonitor(2), manager.GetWorkspaceForWindow(20));
    }

    [Fact]
    public void UpdateMonitorWorkAreas_IgnoresReplacedOrMovedDisplaysUntilTopologyRefresh()
    {
        var manager = CreateManager();
        var existing = manager.ManagedMonitors.ToArray();
        var moved = existing[0] with { Bounds = existing[0].Bounds with { X = -1920 } };
        var replaced = existing[1] with { Handle = 99, WorkArea = existing[1].WorkArea with { Height = 800 } };

        Assert.False(manager.UpdateMonitorWorkAreas([moved, replaced]));
        Assert.Equal(existing, manager.ManagedMonitors);
    }

    [Fact]
    public void MoveWindowToAdjacentWorkspace_UsesDisplayedWorkspaceAndFollowsWindow()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        manager.AddWindow(11);
        manager.FocusWindow(11);

        Assert.True(manager.SetActiveMonitor(2));
        Assert.True(manager.SwitchTo(4));
        manager.AddWindow(20);
        Assert.True(manager.SetActiveMonitor(1));

        Assert.True(manager.MoveWindowToAdjacentWorkspace(11, FocusDirection.Right));

        var source = manager.GetWorkspaceForMonitor(1)!;
        var destination = manager.GetWorkspaceForMonitor(2)!;
        Assert.Equal(1, source.Number);
        Assert.Equal(4, destination.Number);
        Assert.Equal(new nint[] { 10 }, source.LayoutTree.WindowHandles);
        Assert.Equal(new nint[] { 20, 11 }, destination.LayoutTree.WindowHandles);
        Assert.Equal((nint)10, source.SelectedWindowHandle);
        Assert.Same(destination, manager.CurrentWorkspace);
        Assert.Equal((nint)11, destination.SelectedWindowHandle);
        Assert.Equal((nint)11, destination.LayoutTree.FocusedWindowHandle);
    }

    [Fact]
    public void MoveWindowToAdjacentWorkspace_AllowsEmptyDestination()
    {
        var manager = CreateManager();
        Assert.True(manager.SetActiveMonitor(2));
        manager.AddWindow(20);

        Assert.True(manager.MoveWindowToAdjacentWorkspace(20, FocusDirection.Left));

        Assert.Equal((nint)1, manager.ActiveMonitorHandle);
        Assert.Equal((nint)20, Assert.Single(manager.CurrentWorkspace.WindowHandles));
        Assert.Empty(manager.GetWorkspaceForMonitor(2)!.WindowHandles);
    }

    [Fact]
    public void MoveWindowToAdjacentWorkspace_NoMonitorInDirectionLeavesWindowInPlace()
    {
        var manager = CreateManager();
        manager.AddWindow(10);

        Assert.False(manager.MoveWindowToAdjacentWorkspace(10, FocusDirection.Left));
        Assert.False(manager.MoveWindowToAdjacentWorkspace(99, FocusDirection.Right));

        Assert.Equal((nint)1, manager.ActiveMonitorHandle);
        Assert.Equal((nint)10, Assert.Single(manager.CurrentWorkspace.WindowHandles));
        Assert.Empty(manager.GetWorkspaceForMonitor(2)!.WindowHandles);
    }

    [Fact]
    public void ConfigureMonitors_UsesConfiguredWorkspacesAndRoutesHiddenWorkspaceToItsMonitor()
    {
        var manager = new WorkspaceManager();
        var monitors = new[]
        {
            CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false, x: 1920)
        };
        var options = new MonitorManagementOptions
        {
            WorkspaceDeviceNames = new Dictionary<int, string>
            {
                [4] = @"\\.\DISPLAY1",
                [2] = @"\\.\DISPLAY2",
                [6] = @"\\.\display2"
            }
        };

        manager.ConfigureMonitors(monitors, options);
        Assert.Equal(4, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);

        Assert.True(manager.SwitchTo(6));
        Assert.Equal((nint)2, manager.ActiveMonitorHandle);
        manager.ConfigureMonitors(monitors, options);
        Assert.Equal(6, manager.GetWorkspaceForMonitor(2)?.Number);
    }

    [Fact]
    public void ConfigureMonitors_ReloadedAssignmentMovesVisibleWorkspaceWithoutLosingItsWindows()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        manager.ConfigureMonitors(manager.ManagedMonitors, new MonitorManagementOptions
        {
            WorkspaceDeviceNames = new Dictionary<int, string>
            {
                [2] = @"\\.\DISPLAY1",
                [1] = @"\\.\DISPLAY2"
            }
        });

        Assert.Equal(2, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(1, manager.GetWorkspaceForMonitor(2)?.Number);
        Assert.Equal((nint)10, Assert.Single(manager.GetWorkspaceForMonitor(2)!.LayoutTree.WindowHandles));
    }

    [Fact]
    public void ConfigureMonitors_AssignsDifferentWorkspaces()
    {
        var manager = CreateManager();

        Assert.Equal(1, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);
    }

    [Fact]
    public void ConfigureMonitors_PrimaryMonitorBecomesActive()
    {
        var manager = CreateManager();

        Assert.Equal((nint)1, manager.ActiveMonitorHandle);
        Assert.Equal(1, manager.CurrentWorkspace.Number);
    }

    [Fact]
    public void SwitchTo_AlreadyVisibleWorkspace_ActivatesItsMonitor()
    {
        var manager = CreateManager();

        bool result = manager.SwitchTo(2);

        Assert.True(result);
        Assert.Equal((nint)2, manager.ActiveMonitorHandle);
        Assert.Equal(2, manager.CurrentWorkspace.Number);
    }

    [Fact]
    public void SwitchTo_UnassignedWorkspace_ReplacesActiveMonitorWorkspace()
    {
        var manager = CreateManager();

        bool result = manager.SwitchTo(3);

        Assert.True(result);
        Assert.Equal(3, manager.CurrentWorkspace.Number);

        Assert.Equal(3, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);

        Assert.Null(manager.GetMonitorForWorkspace(1));
    }

    [Fact]
    public void SwitchTo_InvalidWorkspace_ReturnsFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.SwitchTo(99));
    }

    [Fact]
    public void PrimaryOnly_ManagesOnlyPrimaryMonitor()
    {
        var manager = new WorkspaceManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
                CreateMonitor(2, @"\\.\DISPLAY2", false)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.PrimaryOnly
            }
        );

        Assert.Single(manager.ManagedMonitors);
        Assert.True(manager.IsManagedMonitor(1));
        Assert.False(manager.IsManagedMonitor(2));
    }

    [Fact]
    public void Selected_ManagesOnlySelectedMonitors()
    {
        var manager = new WorkspaceManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
                CreateMonitor(2, @"\\.\DISPLAY2", false),
                CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.Selected,
                SelectedDeviceNames =
                [
                    @"\\.\DISPLAY2"
                ]
            }
        );

        Assert.Single(manager.ManagedMonitors);

        Assert.False(manager.IsManagedMonitor(1));
        Assert.True(manager.IsManagedMonitor(2));
        Assert.False(manager.IsManagedMonitor(3));

        Assert.Equal((nint)2, manager.ActiveMonitorHandle);
        Assert.Equal(1, manager.CurrentWorkspace.Number);
    }

    [Fact]
    public void Selected_DeviceNameComparison_IsCaseInsensitive()
    {
        var manager = new WorkspaceManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.Selected,
                SelectedDeviceNames =
                [
                    @"\\.\display1"
                ]
            }
        );

        Assert.Single(manager.ManagedMonitors);
        Assert.True(manager.IsManagedMonitor(1));
    }

    [Fact]
    public void ConfigureMonitors_WhenHandleChanges_PreservesWorkspaceAssignments()
    {
        var manager = CreateManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(101, @"\\.\DISPLAY1", true),
            CreateMonitor(202, @"\\.\DISPLAY2", false)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.All
            });

        Assert.Equal(1, manager.GetWorkspaceForMonitor(101)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(202)?.Number);

        Assert.Null(manager.GetWorkspaceForMonitor(1));
        Assert.Null(manager.GetWorkspaceForMonitor(2));
    }

    [Fact]
    public void ConfigureMonitors_WhenMonitorDisconnectsAndReconnects_RestoresWorkspace()
    {
        var manager = CreateManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(101, @"\\.\DISPLAY1", true)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.All
            });

        Assert.Single(manager.ManagedMonitors);
        Assert.Equal((nint)101, manager.GetMonitorForWorkspace(2));

        manager.ConfigureMonitors(
            [
                CreateMonitor(101, @"\\.\DISPLAY1", true),
            CreateMonitor(202, @"\\.\DISPLAY2", false)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.All
            });

        Assert.Equal(2, manager.GetWorkspaceForMonitor(202)?.Number);
        Assert.Equal((nint)202, manager.GetMonitorForWorkspace(2));
    }

    [Fact]
    public void ConfigureMonitors_WhenMonitorIsExcludedAndReincluded_RestoresWorkspace()
    {
        var manager = CreateManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.Selected,
                SelectedDeviceNames = [@"\\.\DISPLAY1"]
            });

        Assert.Single(manager.ManagedMonitors);
        Assert.Equal((nint)1, manager.GetMonitorForWorkspace(2));

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false)
            ],
            new MonitorManagementOptions
            {
                Mode = MonitorManagementMode.All
            });

        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);
    }

    [Fact]
    public void ConfigureMonitors_RepeatedConfiguration_PreservesTemporaryWorkspace()
    {
        var manager = CreateManager();

        var singleMonitor = new[]
        {
        CreateMonitor(1, @"\\.\DISPLAY1", true)
    };

        manager.ConfigureMonitors(
            singleMonitor,
            new MonitorManagementOptions());

        manager.ConfigureMonitors(
            singleMonitor,
            new MonitorManagementOptions());

        Assert.Equal(
            (nint)1,
            manager.GetMonitorForWorkspace(2));

        Assert.Equal(2, manager.GetWorkspaceForMonitor(1)?.Number);
    }

    [Fact]
    public void ConfigureMonitors_WhenActiveMonitorDisconnects_PrioritizesItsWorkspace()
    {
        var manager = CreateManager();

        Assert.True(manager.SetActiveMonitor(2));

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true)
            ],
            new MonitorManagementOptions());

        Assert.Equal(2, manager.CurrentWorkspace.Number);

        Assert.Equal(
            (nint)1,
            manager.GetMonitorForWorkspace(2));

        Assert.Equal(
            2,
            manager.GetWorkspaceForMonitor(1)?.Number);
    }

    [Fact]
    public void SwitchTo_WhileMonitorDisconnected_RestoresOriginalAssignmentsOnReconnect()
    {
        var manager = CreateManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true)
            ],
            new MonitorManagementOptions());

        Assert.Equal(2, manager.GetWorkspaceForMonitor(1)?.Number);

        Assert.True(manager.SwitchTo(3));

        Assert.Equal(3, manager.CurrentWorkspace.Number);
        Assert.Equal((nint)1, manager.GetMonitorForWorkspace(3));

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false)
            ],
            new MonitorManagementOptions());

        Assert.Equal(1, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);

        Assert.Equal((nint)1, manager.GetMonitorForWorkspace(1));
        Assert.Equal((nint)2, manager.GetMonitorForWorkspace(2));

        Assert.Null(manager.GetMonitorForWorkspace(3));
    }

    [Fact]
    public void ConfigureMonitors_MultipleMonitorsReconnectIndividually_RestoresOriginalAssignments()
    {
        var manager = new WorkspaceManager();

        var options = new MonitorManagementOptions();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            options);

        Assert.True(manager.SetActiveMonitor(3));

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true)
            ],
            options);

        Assert.Equal(3, manager.GetWorkspaceForMonitor(1)?.Number);

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false)
            ],
            options);

        Assert.Equal(3, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            options);

        Assert.Equal(1, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);
        Assert.Equal(3, manager.GetWorkspaceForMonitor(3)?.Number);
    }

    [Fact]
    public void SwitchTo_HiddenWorkspace_ActivatesItsPreferredMonitor()
    {
        var manager = CreateManager();

        Assert.True(manager.SwitchTo(3));
        Assert.Equal((nint)1, manager.ActiveMonitorHandle);

        Assert.True(manager.SetActiveMonitor(2));

        Assert.True(manager.SwitchTo(1));
        Assert.Equal((nint)1, manager.ActiveMonitorHandle);

        Assert.True(manager.SetActiveMonitor(2));
        Assert.True(manager.SwitchTo(3));

        Assert.Equal((nint)1, manager.ActiveMonitorHandle);
        Assert.Equal(3, manager.CurrentWorkspace.Number);
    }


    [Fact]
    public void SwitchTo_ConnectedMonitorWorkspace_OverridesTemporaryAssignment()
    {
        var manager = CreateManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true)
            ],
            new MonitorManagementOptions());

        Assert.Equal(2, manager.CurrentWorkspace.Number);

        Assert.True(manager.SwitchTo(1));

        Assert.Equal(1, manager.CurrentWorkspace.Number);

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false)
            ],
            new MonitorManagementOptions());

        Assert.Equal(1, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);
    }

    [Fact]
    public void ConfigureMonitors_NewMonitor_DoesNotStealExistingWorkspace()
    {
        var manager = CreateManager();

        Assert.True(manager.SwitchTo(3));

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            new MonitorManagementOptions());

        Assert.Equal(3, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);
        Assert.Equal(4, manager.GetWorkspaceForMonitor(3)?.Number);
    }

    [Fact]
    public void SwitchTo_WorkspaceOnAnotherMonitor_PreservesTemporaryWorkspaceOnPreviousMonitor()
    {
        var manager = CreateManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            new MonitorManagementOptions());

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            new MonitorManagementOptions());

        Assert.Equal(2, manager.GetWorkspaceForMonitor(1)?.Number);

        Assert.True(manager.SwitchTo(3));
        Assert.Equal((nint)3, manager.ActiveMonitorHandle);

        Assert.True(manager.SetActiveMonitor(1));
        Assert.Equal(2, manager.CurrentWorkspace.Number);
    }

    [Fact]
    public void MoveWorkspaceToMonitor_ChangesWorkspacePreference()
    {
        var manager = CreateManager();

        Assert.True(manager.MoveWorkspaceToMonitor(1, 2));

        Assert.True(manager.SwitchTo(1));

        Assert.Equal((nint)2, manager.ActiveMonitorHandle);
        Assert.Equal(1, manager.CurrentWorkspace.Number);
    }

    [Fact]
    public void MoveWorkspaceToMonitor_InvalidWorkspace_ReturnsFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.MoveWorkspaceToMonitor(99, 1));
    }

    [Fact]
    public void MoveWorkspaceToMonitor_InvalidMonitor_ReturnsFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.MoveWorkspaceToMonitor(1, 99));
    }

    [Fact]
    public void MoveWorkspaceToMonitor_HiddenWorkspace_DoesNotChangeVisibleWorkspaces()
    {
        var manager = CreateManager();

        Assert.True(manager.SwitchTo(3));

        Assert.True(manager.SwitchTo(1));

        Assert.True(manager.MoveWorkspaceToMonitor(3, 2));

        Assert.Equal(1, manager.GetWorkspaceForMonitor(1)?.Number);
        Assert.Equal(2, manager.GetWorkspaceForMonitor(2)?.Number);

        Assert.True(manager.SwitchTo(3));

        Assert.Equal((nint)2, manager.ActiveMonitorHandle);
        Assert.Equal(3, manager.CurrentWorkspace.Number);
    }

    [Fact]
    public void MoveWorkspaceToMonitor_TemporarilyDisplayedWorkspace_ChangesPermanentOwnership()
    {
        var manager = CreateManager();

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            new MonitorManagementOptions());

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            new MonitorManagementOptions());

        Assert.Equal(2, manager.GetWorkspaceForMonitor(1)?.Number);

        Assert.True(manager.MoveWorkspaceToMonitor(2, 3));

        Assert.Equal(2, manager.GetWorkspaceForMonitor(3)?.Number);
        Assert.Equal(1, manager.GetWorkspaceForMonitor(1)?.Number);

        manager.ConfigureMonitors(
            [
                CreateMonitor(1, @"\\.\DISPLAY1", true),
            CreateMonitor(2, @"\\.\DISPLAY2", false),
            CreateMonitor(3, @"\\.\DISPLAY3", false)
            ],
            new MonitorManagementOptions());

        Assert.Equal(2, manager.GetWorkspaceForMonitor(3)?.Number);
        Assert.NotEqual(2, manager.GetWorkspaceForMonitor(2)?.Number);
    }


    [Fact]
    public void AddWindow_AddsWindowToLayoutTree()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);

        Assert.Contains((nint)1, manager.CurrentWorkspace.LayoutTree.WindowHandles);
    }

    [Fact]
    public void AddWindow_PreservesExistingSelection()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);
        manager.AddWindow((nint)2);

        Assert.Equal((nint)1, manager.CurrentWorkspace.SelectedWindowHandle);
        Assert.Equal((nint)1, manager.CurrentWorkspace.LayoutTree.FocusedWindowHandle);
    }

    [Fact]
    public void RemoveWindow_RemovesWindowFromLayoutTree()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);
        manager.AddWindow((nint)2);

        manager.RemoveWindow((nint)1);

        Assert.DoesNotContain(
            (nint)1,
            manager.CurrentWorkspace.LayoutTree.WindowHandles);

        Assert.Equal((nint)2, manager.CurrentWorkspace.LayoutTree.FocusedWindowHandle);
    }

    [Fact]
    public void ToggleFloating_RemovesWindowFromLayoutTree()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);

        var result = manager.ToggleFloating((nint)1);

        Assert.True(result);
        Assert.Contains((nint)1, manager.CurrentWorkspace.FloatingWindowHandles);
        Assert.DoesNotContain((nint)1, manager.CurrentWorkspace.LayoutTree.WindowHandles);
    }

    [Fact]
    public void ToggleFloating_BackToTiled_AddsWindowToLayoutTree()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);
        manager.ToggleFloating((nint)1);

        var result = manager.ToggleFloating((nint)1);

        Assert.True(result);
        Assert.DoesNotContain((nint)1, manager.CurrentWorkspace.FloatingWindowHandles);
        Assert.Contains((nint)1, manager.CurrentWorkspace.LayoutTree.WindowHandles);
    }

    [Fact]
    public void MoveWindowToWorkspace_MovesWindowBetweenTrees()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);

        var result = manager.MoveWindowToWorkspace((nint)1, 2);

        Assert.True(result);

        Assert.DoesNotContain(
            (nint)1,
            manager.Workspaces.Single(w => w.Number == 1).LayoutTree.WindowHandles);

        Assert.Contains(
            (nint)1,
            manager.Workspaces.Single(w => w.Number == 2).LayoutTree.WindowHandles);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(10, 99)]
    [InlineData(99, 2)]
    public void MoveWindowToWorkspace_RejectsInvalidMovesWithoutChangingMembershipOrFocus(int handle, int destination)
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        manager.AddWindow(11);
        Assert.True(manager.FocusWindow(10));
        var source = manager.CurrentWorkspace;
        var target = manager.GetWorkspaceForMonitor(2)!;
        var focusedNode = source.LayoutTree.FocusedNode;

        Assert.False(manager.MoveWindowToWorkspace(handle, destination));

        Assert.Equal(new nint[] { 10, 11 }, source.WindowHandles);
        Assert.Equal(new nint[] { 10, 11 }, source.LayoutTree.WindowHandles);
        Assert.Equal((nint)10, source.SelectedWindowHandle);
        Assert.Same(focusedNode, source.LayoutTree.FocusedNode);
        Assert.Empty(target.WindowHandles);
        Assert.Empty(target.LayoutTree.WindowHandles);
        Assert.Null(target.SelectedWindowHandle);
        Assert.Equal((nint)1, manager.ActiveMonitorHandle);
    }

    [Fact]
    public void MoveWindowToWorkspace_MovingSelectionChoosesSourceReplacementAndKeepsTargetSelection()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        manager.AddWindow(11);
        Assert.True(manager.FocusWindow(10));
        var source = manager.CurrentWorkspace;
        Assert.True(manager.SwitchTo(2));
        manager.AddWindow(20);
        var target = manager.CurrentWorkspace;
        Assert.True(manager.SwitchTo(1));

        Assert.True(manager.MoveWindowToWorkspace(10, 2));

        Assert.Equal((nint)11, Assert.Single(source.WindowHandles));
        Assert.Equal((nint)11, source.SelectedWindowHandle);
        Assert.Equal((nint)11, source.LayoutTree.FocusedWindowHandle);
        Assert.Equal((nint)20, target.SelectedWindowHandle);
        Assert.Equal((nint)20, target.LayoutTree.FocusedWindowHandle);
        Assert.Contains((nint)10, target.LayoutTree.WindowHandles);
        Assert.Equal((nint)1, manager.ActiveMonitorHandle);
    }

    [Fact]
    public void SelectWindowPreservingContainerFocus_UpdatesWindowSelectionWhileKeepingStructuralFocus()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        manager.AddWindow(11);
        var tree = manager.CurrentWorkspace.LayoutTree;
        Assert.True(tree.FocusParent());
        var parent = tree.FocusedNode;
        Assert.NotNull(parent);
        Assert.False(parent.IsLeaf);

        Assert.True(manager.SelectWindowPreservingContainerFocus(11));
        Assert.Equal((nint)11, manager.CurrentWorkspace.SelectedWindowHandle);
        Assert.Equal((nint)11, tree.FocusedWindowHandle);
        Assert.Same(parent, tree.FocusedNode);

        Assert.True(manager.FocusWindow(11));
        Assert.Same(tree.Root.FindWindow(11), tree.FocusedNode);
    }

    [Fact]
    public void IsWindowVisible_FollowsWorkspaceDisplayAcrossMonitors()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        Assert.True(manager.SwitchTo(2));
        manager.AddWindow(20);
        Assert.True(manager.ToggleFloating(20));
        Assert.True(manager.SwitchTo(1));

        Assert.True(manager.IsWindowVisible(10));
        Assert.True(manager.IsWindowVisible(20));

        Assert.True(manager.SwitchTo(3));
        Assert.False(manager.IsWindowVisible(10));
        Assert.True(manager.IsWindowVisible(20));
        Assert.False(manager.IsWindowVisible(99));

        Assert.True(manager.SwitchTo(1));
        Assert.True(manager.IsWindowVisible(10));
    }

    [Fact]
    public void LayoutPreview_TabDropDoesNotChangeTheCommittedTreeOrFocus()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        manager.AddWindow(11);
        var workspace = manager.CurrentWorkspace;
        Assert.True(workspace.LayoutTree.FocusWindow(11));
        Assert.True(workspace.LayoutTree.SetFocusedGroupLayout(LayoutType.Tabbed));
        var area = new LayoutArea(0, 0, 900, 600);
        var original = ContainerLayoutEngine.CalculateLayout(workspace.LayoutTree, area);
        var header = Assert.Single(original.Headers);

        var preview = workspace.CopyForLayoutPreview();
        Assert.NotSame(workspace.LayoutTree.Root, preview.LayoutTree.Root);
        Assert.Equal(workspace.LayoutTree.FocusedNodeId, preview.LayoutTree.FocusedNodeId);
        Assert.True(preview.LayoutTree.DropWindowOnTab(10, header.NodeId, 11));
        var proposed = ContainerLayoutEngine.CalculateLayout(preview.LayoutTree, area);

        Assert.Equal(new nint[] { 11, 10 }, Assert.Single(proposed.Headers).WindowHandles);
        Assert.Equal((nint)10, Assert.Single(proposed.WindowPlacements).Handle);
        Assert.Equal(new nint[] { 10, 11 }, workspace.LayoutTree.WindowHandles);
        Assert.Equal((nint)11, workspace.LayoutTree.FocusedWindowHandle);
        Assert.Equal(original.WindowPlacements,
            ContainerLayoutEngine.Calculate(workspace.LayoutTree, area));
    }

    [Fact]
    public void LayoutPreview_MonitorTransferMovesOnlyTheCopiedWorkspaceMembership()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        manager.AddWindow(11);
        var source = manager.CurrentWorkspace;
        var target = manager.GetWorkspaceForMonitor(2)!;
        var sourcePreview = source.CopyForLayoutPreview();
        var targetPreview = target.CopyForLayoutPreview();

        Assert.True(WorkspaceManager.TransferWindow(sourcePreview, targetPreview, 10));

        Assert.Equal((nint)11, Assert.Single(sourcePreview.LayoutTree.WindowHandles));
        Assert.Equal((nint)10, Assert.Single(targetPreview.LayoutTree.WindowHandles));
        Assert.Equal(new nint[] { 10, 11 }, source.LayoutTree.WindowHandles);
        Assert.Empty(target.WindowHandles);
        Assert.Same(source, manager.GetWorkspaceForWindow(10));
        Assert.Equal((nint)1, manager.ActiveMonitorHandle);
    }

    [Fact]
    public void MoveFloatingWindowToMonitor_PrimaryWorkspaceSwitchKeepsItOnSecondaryWorkspace()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        Assert.True(manager.ToggleFloating(10));

        Assert.True(manager.MoveFloatingWindowToMonitor(10, 2));
        Assert.True(manager.SwitchTo(3));

        var owner = manager.GetWorkspaceForWindow(10)!;
        Assert.Equal(2, owner.Number);
        Assert.Equal((nint)2, manager.GetMonitorForWorkspace(owner.Number));
        Assert.Contains((nint)10, owner.FloatingWindowHandles);
        Assert.DoesNotContain((nint)10, owner.LayoutTree.WindowHandles);
        Assert.Equal(3, manager.CurrentWorkspace.Number);
    }

    [Fact]
    public void MoveFloatingWindowToMonitor_RejectsTiledWindowAndInvalidDestination()
    {
        var manager = CreateManager();
        manager.AddWindow(10);
        Assert.False(manager.MoveFloatingWindowToMonitor(10, 2));
        Assert.True(manager.ToggleFloating(10));
        Assert.False(manager.MoveFloatingWindowToMonitor(10, 99));
        Assert.False(manager.MoveFloatingWindowToMonitor(10, 1));
        Assert.Same(manager.CurrentWorkspace, manager.GetWorkspaceForWindow(10));
    }

    [Theory]
    [InlineData(MonitorManagementMode.PrimaryOnly, false)]
    [InlineData(MonitorManagementMode.PrimaryOnly, true)]
    [InlineData(MonitorManagementMode.Selected, false)]
    [InlineData(MonitorManagementMode.Selected, true)]
    public void ReleaseWindowToUnmanagedMonitor_RemovesWorkspaceOwnershipAndPreservesOtherTiles(
        MonitorManagementMode mode, bool floating)
    {
        var manager = new WorkspaceManager();
        manager.ConfigureMonitors(
            [CreateMonitor(1, @"\\.\DISPLAY1", true), CreateMonitor(2, @"\\.\DISPLAY2", false, x: 1920)],
            new MonitorManagementOptions { Mode = mode, SelectedDeviceNames = [@"\\.\DISPLAY1"] });
        manager.AddWindow(10);
        manager.AddWindow(11);
        Assert.True(manager.FocusWindow(10));
        if (floating)
            Assert.True(manager.ToggleFloating(10));
        var source = manager.CurrentWorkspace;

        Assert.True(manager.ReleaseWindowToUnmanagedMonitor(10, 2));

        Assert.Null(manager.GetWorkspaceForWindow(10));
        Assert.Empty(source.FloatingWindowHandles);
        Assert.Equal((nint)11, Assert.Single(source.WindowHandles));
        Assert.Equal((nint)11, Assert.Single(source.LayoutTree.WindowHandles));
        Assert.Equal((nint)11, source.SelectedWindowHandle);
        Assert.True(manager.SwitchTo(3));
        Assert.Null(manager.GetWorkspaceForWindow(10));

        manager.AddWindow(10);
        Assert.Equal(3, manager.GetWorkspaceForWindow(10)?.Number);
        Assert.Equal((nint)10, Assert.Single(manager.CurrentWorkspace.LayoutTree.WindowHandles));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(10, 2)]
    [InlineData(99, 3)]
    public void ReleaseWindowToUnmanagedMonitor_InvalidReleaseKeepsWorkspaceOwnership(int handle, int monitor)
    {
        var manager = CreateManager();
        manager.AddWindow(10);

        Assert.False(manager.ReleaseWindowToUnmanagedMonitor(handle, monitor));
        Assert.Same(manager.CurrentWorkspace, manager.GetWorkspaceForWindow(10));
        Assert.Equal((nint)10, Assert.Single(manager.CurrentWorkspace.LayoutTree.WindowHandles));
    }

    [Fact]
    public void MoveFloatingWindowToWorkspace_KeepsWindowFloating()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);
        manager.ToggleFloating((nint)1);

        var result = manager.MoveWindowToWorkspace((nint)1, 2);

        var target = manager.Workspaces.Single(w => w.Number == 2);

        Assert.True(result);
        Assert.Contains((nint)1, target.WindowHandles);
        Assert.Contains((nint)1, target.FloatingWindowHandles);
        Assert.DoesNotContain((nint)1, target.LayoutTree.WindowHandles);
    }

    [Fact]
    public void RemoveWindow_UnrelatedWindow_PreservesPendingSplit()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);
        manager.AddWindow((nint)2);

        var workspace = manager.CurrentWorkspace;
        var tree = workspace.LayoutTree;

        Assert.True(tree.FocusParent());
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));

        var focusedNodeId = tree.FocusedNodeId;

        manager.RemoveWindow((nint)999);

        Assert.Equal(focusedNodeId, tree.FocusedNodeId);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.FocusedNode?.PendingSplitOrientation);
    }

    [Fact]
    public void RemoveWindow_UnrelatedWindow_AddWindowUsesPendingSplit()
    {
        var manager = new WorkspaceManager();

        manager.AddWindow((nint)1);
        manager.AddWindow((nint)2);

        var workspace = manager.CurrentWorkspace;
        var tree = workspace.LayoutTree;

        Assert.True(tree.FocusParent());
        Assert.True(tree.SplitFocused(ContainerOrientation.Vertical));

        manager.RemoveWindow((nint)999);
        manager.AddWindow((nint)3);

        Assert.Equal(
            ContainerOrientation.Vertical,
            tree.Root.Orientation);

        Assert.True(tree.Root.IsSplit);
        Assert.Equal(tree.Root.Id, tree.FocusedNodeId);
        Assert.Equal(3, tree.WindowHandles.Count);
    }
}
