using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.User32;

internal static partial class WindowPaintingApi
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseTracking
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TrackMouseEvent(ref MouseTracking tracking);

    [StructLayout(LayoutKind.Sequential)]
    internal struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(
        uint action, uint size, ref HighContrast value, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(uint action, uint size, out int value, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint window, nint dc);

    [LibraryImport("user32.dll")]
    internal static partial uint GetSysColor(int index);

    [LibraryImport("user32.dll")]
    internal static partial nint BeginPaint(
        nint hwnd,
        out NativePaintStruct paint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndPaint(
        nint hwnd,
        in NativePaintStruct paint);

    [LibraryImport("user32.dll")]
    internal static partial nint GetSysColorBrush(int index);

    [LibraryImport("user32.dll")]
    internal static partial int FillRect(
        nint hdc,
        in NativeWindowRect rect,
        nint brush);

    [LibraryImport(
        "user32.dll",
        EntryPoint = "DrawTextW",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DrawText(
        nint hdc,
        string text,
        int length,
        ref NativeWindowRect rect,
        uint format);
}
