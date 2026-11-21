using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.Kernel32;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

public enum TrayMenuIcon
{
    None,
    Settings,
    Refresh,
    Repair,
    Power
}

public sealed record TrayMenuItem(string Label, Action? Action, TrayMenuIcon Icon = TrayMenuIcon.None,
    string? Shortcut = null);

public sealed class NativeTrayIcon : IDisposable
{
    private const string ClassName = "OrtiloNotificationIcon";
    private const uint CallbackMessage = 0x8006;
    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);

    private static readonly WindowProcedure Procedure = WndProc;
    private static NativeTrayIcon? _instance;
    private static bool _classRegistered;

    private readonly Func<IReadOnlyList<TrayMenuItem>> _getMenu;
    private readonly uint _taskbarCreated;
    private nint _window;
    private bool _added;
    private bool _version4;
    private readonly NativeTrayMenu _menu;

    public NativeTrayIcon(Func<IReadOnlyList<TrayMenuItem>> getMenu)
    {
        if (_instance is not null)
            throw new InvalidOperationException("A Ortilo tray icon is already active.");

        _getMenu = getMenu;
        _taskbarCreated = TrayApi.RegisterWindowMessage("TaskbarCreated");
        if (!_classRegistered)
        {
            NativeWindowClassRegistrar.Register(ClassName, Marshal.GetFunctionPointerForDelegate(Procedure));
            _classRegistered = true;
        }

        const uint toolWindow = 0x00000080;
        _window = WindowClassApi.CreateWindowEx(toolWindow, ClassName, "Ortilo tray",
            NativeBackdrop.FramedWindowStyle, 0, 0, 0, 0,
            nint.Zero, nint.Zero, ModuleApi.GetModuleHandle(null), nint.Zero);
        if (_window == nint.Zero)
            throw new InvalidOperationException($"Could not create the Ortilo tray window. Win32 error {Marshal.GetLastPInvokeError()}.");

        _menu = new NativeTrayMenu(_window);
        _instance = this;
        AddIcon();
    }

    public static bool IsInternalWindow(nint handle) =>
        handle != nint.Zero && _instance is { } tray &&
        (tray._window == handle || tray._menu.IsInternalWindow(handle));

    private NotifyIconData IconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _window,
        Id = 1
    };

    private unsafe void AddIcon()
    {
        var data = IconData();
        const uint messageIconTipAndShowTip = 0x00000001 | 0x00000002 | 0x00000004 | 0x00000080;
        data.Flags = messageIconTipAndShowTip;
        data.CallbackMessage = CallbackMessage;
        const int applicationIcon = 32512;
        const uint imageIcon = 1, shared = 0x8000;
        const int smallIconWidth = 49, smallIconHeight = 50;
        data.Icon = TrayApi.LoadImage(ModuleApi.GetModuleHandle(null), applicationIcon, imageIcon,
            TrayApi.GetSystemMetrics(smallIconWidth), TrayApi.GetSystemMetrics(smallIconHeight), shared);
        if (data.Icon == nint.Zero)
            data.Icon = TrayApi.LoadIcon(nint.Zero, applicationIcon);
        if (data.Icon == nint.Zero)
        {
            Console.Error.WriteLine("Could not load the Ortilo notification area icon.");
            return;
        }

        "Ortilo".AsSpan().CopyTo(new Span<char>(data.Tip, 128));
        _added = TrayApi.NotifyIcon(NIM_ADD, ref data);
        if (!_added)
        {
            Console.Error.WriteLine("Could not add Ortilo to the Windows notification area.");
            return;
        }

        data.Version = 4;
        _version4 = TrayApi.NotifyIcon(NIM_SETVERSION, ref data);
    }

    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (NativeBackdrop.HandleFrameMessage(message, out nint frameResult))
            return frameResult;

        var tray = _instance;
        if (tray is null || tray._window != hwnd)
            return WindowClassApi.DefWindowProc(hwnd, message, wParam, lParam);

        try
        {
            if (tray._taskbarCreated != 0 && message == tray._taskbarCreated)
            {
                tray.AddIcon();
                return nint.Zero;
            }

            if (message == CallbackMessage)
            {
                uint notification = (uint)lParam & 0xFFFF;
                if (tray._version4 && (notification is WM_CONTEXTMENU or NIN_SELECT or NIN_KEYSELECT))
                    tray._menu.Show(unchecked((short)wParam), unchecked((short)(wParam >> 16)),
                        tray._getMenu(), keyboard: notification == NIN_KEYSELECT);
                else if (!tray._version4 && (notification is WM_LBUTTONUP or WM_RBUTTONUP) &&
                         WindowApi.GetCursorPos(out var cursor))
                    tray._menu.Show(cursor.X, cursor.Y, tray._getMenu(), keyboard: false);

                return nint.Zero;
            }

            if (tray._menu.HandleMessage(message, wParam, lParam, out nint result))
                return result;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Could not use the Ortilo tray menu. {exception.Message}");
        }

        return WindowClassApi.DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_window == nint.Zero)
            return;

        _menu.Dispose();
        if (_added)
        {
            var data = IconData();
            TrayApi.NotifyIcon(NIM_DELETE, ref data);
        }

        if (!WindowApi.DestroyWindow(_window))
            Console.Error.WriteLine($"Could not destroy the Ortilo tray window. Win32 error {Marshal.GetLastPInvokeError()}.");

        _window = nint.Zero;
        _instance = null;
    }
}
