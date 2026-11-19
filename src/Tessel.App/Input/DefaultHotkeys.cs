using Tessel.Platform.Windows.Input;

namespace Tessel.App.Input;

public static class DefaultHotkeys
{
    private const uint LeftArrow = 0x25;
    private const uint UpArrow = 0x26;
    private const uint RightArrow = 0x27;
    private const uint DownArrow = 0x28;

    private static readonly HotkeyModifiers Alt =
        HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat;

    private static readonly HotkeyModifiers AltShift =
        HotkeyModifiers.Alt |
        HotkeyModifiers.Shift |
        HotkeyModifiers.NoRepeat;

    private static readonly HotkeyModifiers AltControlShift =
        HotkeyModifiers.Alt |
        HotkeyModifiers.Control |
        HotkeyModifiers.Shift |
        HotkeyModifiers.NoRepeat;

    private static readonly HotkeyModifiers AltControl =
        HotkeyModifiers.Alt |
        HotkeyModifiers.Control |
        HotkeyModifiers.NoRepeat;

    public static IReadOnlyList<HotkeyDefinition> Create()
    {
        var hotkeys = new List<HotkeyDefinition>();

        void Add(
            string action,
            uint key,
            HotkeyModifiers modifiers,
            int? argument = null)
        {
            hotkeys.Add(new HotkeyDefinition(
                action,
                key,
                modifiers,
                argument
            ));
        }

        Add(HotkeyActions.Reapply, '0', AltShift);
        Add(HotkeyActions.ReloadConfig, 'R', AltControl);
        Add(HotkeyActions.Reset, 'R', AltControlShift);

        Add(HotkeyActions.FocusLeft, 'H', Alt);
        Add(HotkeyActions.FocusDown, 'J', Alt);
        Add(HotkeyActions.FocusUp, 'K', Alt);
        Add(HotkeyActions.FocusRight, 'L', Alt);

        Add(HotkeyActions.FocusLeft, LeftArrow, Alt);
        Add(HotkeyActions.FocusUp, UpArrow, Alt);
        Add(HotkeyActions.FocusRight, RightArrow, Alt);
        Add(HotkeyActions.FocusDown, DownArrow, Alt);

        Add(HotkeyActions.MoveLeft, 'H', AltShift);
        Add(HotkeyActions.MoveDown, 'J', AltShift);
        Add(HotkeyActions.MoveUp, 'K', AltShift);
        Add(HotkeyActions.MoveRight, 'L', AltShift);

        Add(HotkeyActions.MoveLeft, LeftArrow, AltShift);
        Add(HotkeyActions.MoveUp, UpArrow, AltShift);
        Add(HotkeyActions.MoveRight, RightArrow, AltShift);
        Add(HotkeyActions.MoveDown, DownArrow, AltShift);

        for (int number = 1; number <= 9; number++)
        {
            uint key = (uint)('0' + number);

            Add(
                HotkeyActions.SwitchWorkspace,
                key,
                Alt,
                number
            );

            Add(
                HotkeyActions.MoveToWorkspace,
                key,
                AltShift,
                number
            );
        }
        Add(
            HotkeyActions.MoveWorkspaceToNextMonitor,
            RightArrow,
            AltControlShift
        );
        Add(
            HotkeyActions.MoveWorkspaceToPreviousMonitor,
            LeftArrow,
            AltControlShift
        );

        Add(HotkeyActions.MasterStack, 'E', Alt);
        Add(HotkeyActions.Stacking, 'S', Alt);
        Add(HotkeyActions.Tabbed, 'W', Alt);
        Add(HotkeyActions.ContainerTree, 'T', Alt);

        Add(HotkeyActions.FocusParent, 'A', Alt);
        Add(HotkeyActions.FocusChild, 'D', Alt);
        Add(HotkeyActions.ToggleSplit, 'V', Alt);

        Add(HotkeyActions.ResizeLeft, LeftArrow, AltControl);
        Add(HotkeyActions.ResizeRight, RightArrow, AltControl);
        Add(HotkeyActions.ResizeUp, UpArrow, AltControl);
        Add(HotkeyActions.ResizeDown, DownArrow, AltControl);

        Add(HotkeyActions.Fullscreen, 'F', Alt);
        Add(HotkeyActions.Minimize, 'M', Alt);
        Add(HotkeyActions.ToggleFloating, 0x20, AltShift);

        return hotkeys;
    }
}
