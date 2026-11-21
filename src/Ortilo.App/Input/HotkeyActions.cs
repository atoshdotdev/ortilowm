namespace Ortilo.App.Input;

public static class HotkeyActions
{
    public const string Reapply = "layout.reapply";
    public const string ReloadConfig = "app.reload-config";
    public const string Reset = "app.reset";

    public const string MasterStack = "layout.master-stack";
    public const string Stacking = "layout.stacking";
    public const string Tabbed = "layout.tabbed";
    public const string ContainerTree = "layout.container-tree";

    public const string ResizeLeft = "window.resize-left";
    public const string ResizeRight = "window.resize-right";
    public const string ResizeUp = "window.resize-up";
    public const string ResizeDown = "window.resize-down";

    public const string FocusParent = "focus.parent";
    public const string FocusChild = "focus.child";
    public const string ToggleSplit = "container.toggle-split";

    public const string FocusLeft = "focus.left";
    public const string FocusRight = "focus.right";
    public const string FocusUp = "focus.up";
    public const string FocusDown = "focus.down";

    public const string MoveLeft = "move.left";
    public const string MoveRight = "move.right";
    public const string MoveUp = "move.up";
    public const string MoveDown = "move.down";

    public const string SwitchWorkspace = "workspace.switch";
    public const string MoveToWorkspace = "workspace.move";
    public const string MoveWorkspaceToNextMonitor =
    "workspace.move-to-next-monitor";
    public const string MoveWorkspaceToPreviousMonitor =
        "workspace.move-to-previous-monitor";

    public const string Fullscreen = "window.fullscreen";
    public const string Minimize = "window.minimize";
    public const string ToggleFloating = "window.toggle-floating";
}
