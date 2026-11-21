using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Interop.User32;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct NativeMonitorInfo
{
    public uint Size;
    public NativeWindowRect Monitor;
    public NativeWindowRect Work;
    public uint Flags;

    public fixed char DeviceName[32];
}

internal static unsafe partial class MonitorEnumerationApi
{
    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int EnumDisplayMonitors(
        nint hdc,
        nint clipRect,
        delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int> callback,
        nint data
    );

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(
        nint hMonitor,
        ref NativeMonitorInfo monitorInfo
    );

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(
        nint hwnd,
        uint flags
    );
}
