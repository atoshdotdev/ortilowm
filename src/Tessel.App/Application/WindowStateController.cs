using Tessel.Core.Services;
using Tessel.Platform.Windows.Windows;

namespace Tessel.App.Application;

public sealed class WindowStateController
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly Action _applyLayout;

    public WindowStateController(
        WorkspaceManager workspaceManager,
        Action applyLayout)
    {
        _workspaceManager = workspaceManager;
        _applyLayout = applyLayout;
    }

    public void ToggleFloating()
    {
        nint handle = WindowFocuser.GetForegroundHandle();

        if (handle == nint.Zero)
            return;

        bool success = _workspaceManager.ToggleFloating(handle);

        if (!success)
            return;

        bool isFloating =
            _workspaceManager.CurrentWorkspace.FloatingWindowHandles
                .Contains(handle);

        _applyLayout();
    }

    public void ToggleFullscreen()
    {
        nint handle = WindowFocuser.GetForegroundHandle();

        if (handle == nint.Zero)
            return;

        bool success = FullscreenManager.Toggle(handle);

        if (success)
            _applyLayout();
    }

    public void MinimizeFocusedWindow()
    {
        nint handle = WindowFocuser.GetForegroundHandle();

        if (handle == nint.Zero)
            return;

        if (!_workspaceManager.CurrentWorkspace.WindowHandles.Contains(handle))
            return;

        WindowVisibility.Minimize(handle);
    }
}
