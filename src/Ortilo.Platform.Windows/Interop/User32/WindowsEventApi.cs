using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Interop.User32;

internal static partial class WindowEventApi
{
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint hmodWinEventProc,
        nint pfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint flags
    );

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWinEvent(nint hWinEventHook);
}
