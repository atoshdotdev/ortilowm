using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Interop.User32;

internal static partial class WindowMessageApi
{
    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    internal static partial int GetMessage(
        out NativeMessage message,
        nint hwnd,
        uint min,
        uint max
    );

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(
        in NativeMessage message
    );

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static partial nint DispatchMessage(
        in NativeMessage message
    );

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PeekMessage(
        out NativeMessage message,
        nint hwnd,
        uint min,
        uint max,
        uint remove
    );

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessage(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam
    );
}
