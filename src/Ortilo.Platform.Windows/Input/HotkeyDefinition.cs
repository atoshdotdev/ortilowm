namespace Ortilo.Platform.Windows.Input;

public sealed record HotkeyDefinition(
    string ActionName,
    uint VirtualKey,
    HotkeyModifiers Modifiers,
    int? Argument = null
);
