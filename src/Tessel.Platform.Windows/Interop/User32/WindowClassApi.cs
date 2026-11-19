using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.User32;

internal static partial class WindowClassApi
{
    [LibraryImport(
        "user32.dll",
        EntryPoint = "RegisterClassExW",
        SetLastError = true)]
    internal static partial ushort RegisterClassEx(
        ref NativeWindowClass windowClass);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "CreateWindowExW",
        StringMarshalling = StringMarshalling.Utf16,
        SetLastError = true)]
    internal static partial nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam);
}
