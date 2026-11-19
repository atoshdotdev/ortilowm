using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.User32;

internal static unsafe partial class WindowEnumerationApi
{
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int EnumWindows(
        delegate* unmanaged[Stdcall]<nint, nint, int> callback,
        nint lParam
    );

    [LibraryImport("user32.dll")]
    internal static partial int IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    internal static partial int GetWindowTextLength(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    internal static partial int GetWindowText(
        nint hWnd,
        char* text,
        int maxCount
    );

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(
        nint hWnd,
        out uint processId
    );

    [LibraryImport("user32.dll")]
    internal static partial nint GetWindow(
        nint hWnd,
        uint command
    );

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    internal static partial int GetWindowLong(
        nint hWnd,
        int index
    );

    [LibraryImport("user32.dll")]
    internal static partial int IsIconic(nint hWnd);
}
