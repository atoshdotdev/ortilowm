using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Input;

public sealed partial class HotkeyManager : IDisposable
{
    private const int FirstHotkeyId = 1;
    private const int LastHotkeyId = 0xBFFF;

    private readonly Dictionary<int, HotkeyDefinition> _registered = [];

    private bool _isRegistered;
    private bool _disposed;
    private int _nextId = FirstHotkeyId;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(
        nint hwnd,
        int id,
        uint modifiers,
        uint virtualKey
    );

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(
        nint hwnd,
        int id
    );

    public void RegisterAll(IEnumerable<HotkeyDefinition> definitions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isRegistered)
            throw new InvalidOperationException(
                "Hotkeys have already been registered."
            );

        _isRegistered = true;

        try
        {
            foreach (var definition in definitions)
            {
                if (string.IsNullOrWhiteSpace(definition.ActionName))
                    throw new ArgumentException(
                        "Hotkey action name cannot be empty."
                    );

                if (definition.VirtualKey == 0)
                    throw new ArgumentException(
                        "Hotkey virtual key cannot be zero."
                    );

                if (_nextId > LastHotkeyId)
                    throw new InvalidOperationException(
                        "Maximum number of hotkeys exceeded."
                    );

                int id = _nextId++;

                if (!RegisterHotKey(
                    nint.Zero,
                    id,
                    (uint)definition.Modifiers,
                    definition.VirtualKey))
                {
                    throw new Win32Exception(
                        Marshal.GetLastPInvokeError(),
                        $"Could not register hotkey: {definition.ActionName}"
                    );
                }

                _registered.Add(id, definition);
            }
        }
        catch
        {
            UnregisterAll();
            _isRegistered = false;
            throw;
        }
    }

    public HotkeyDefinition? GetDefinition(int id)
    {
        return _registered.GetValueOrDefault(id);
    }

    public HotkeyDefinition? GetBinding(string actionName) =>
        _registered.Values.FirstOrDefault(binding => binding.ActionName == actionName);

    public void ReplaceAll(IReadOnlyList<HotkeyDefinition> definitions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var existing = _registered.ToDictionary(pair =>
            (pair.Value.VirtualKey, pair.Value.Modifiers), pair => pair.Key);
        var replacement = new Dictionary<int, HotkeyDefinition>();
        var added = new List<int>();
        try
        {
            foreach (var definition in definitions)
            {
                if (!existing.TryGetValue((definition.VirtualKey, definition.Modifiers), out int id))
                {
                    if (_nextId > LastHotkeyId)
                        throw new InvalidOperationException("Maximum number of hotkeys exceeded.");

                    id = _nextId++;
                    if (!RegisterHotKey(nint.Zero, id, (uint)definition.Modifiers, definition.VirtualKey))
                        throw new Win32Exception(Marshal.GetLastPInvokeError(),
                            $"Could not register hotkey: {definition.ActionName}");
                    added.Add(id);
                }

                replacement.Add(id, definition);
            }
        }
        catch
        {
            foreach (int id in added)
                UnregisterHotKey(nint.Zero, id);
            throw;
        }

        foreach (int id in _registered.Keys)
        {
            if (!replacement.ContainsKey(id))
                UnregisterHotKey(nint.Zero, id);
        }

        _registered.Clear();
        foreach (var (id, definition) in replacement)
            _registered.Add(id, definition);
        _isRegistered = true;
    }

    private void UnregisterAll()
    {
        foreach (int id in _registered.Keys)
            UnregisterHotKey(nint.Zero, id);

        _registered.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        UnregisterAll();

        _disposed = true;
    }
}
