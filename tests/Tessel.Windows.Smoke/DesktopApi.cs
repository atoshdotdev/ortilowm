using System.Runtime.InteropServices;

internal static partial class DesktopApi
{
    internal const uint QuitMessage = 0x0012;

    internal static bool HasVisibleAppWindows()
    {
        bool found = false;
        int result = EnumWindows((window, _) =>
        {
            const uint owner = 4;
            const int extendedStyle = -20;
            const int toolWindow = 0x80;
            if (IsWindowVisible(window) && GetWindowTextLength(window) > 0 &&
                GetWindow(window, owner) == 0 && (GetWindowLong(window, extendedStyle) & toolWindow) == 0 &&
                !(DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0))
            {
                found = true;
                return 0;
            }
            return 1;
        }, 0);
        if (result == 0 && !found)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Desktop enumeration failed.");
        return found;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumWindowsCallback(nint window, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int EnumWindows(EnumWindowsCallback callback, nint data);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLength(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindow(string className, string? title);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint FindWindowEx(nint parent, nint previous, string className, string? title);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll")]
    internal static partial void NotifyWinEvent(uint eventId, nint window, int objectId, int childId);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
}
