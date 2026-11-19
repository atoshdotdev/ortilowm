using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.User32;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NotifyIconData
{
    internal uint Size;
    internal nint Window;
    internal uint Id;
    internal uint Flags;
    internal uint CallbackMessage;
    internal nint Icon;
    internal fixed char Tip[128];
    internal uint State;
    internal uint StateMask;
    internal fixed char Info[256];
    internal uint Version;
    internal fixed char InfoTitle[64];
    internal uint InfoFlags;
    internal Guid Guid;
    internal nint BalloonIcon;
}

internal static partial class TrayApi
{
    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool NotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint RegisterWindowMessage(string message);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    internal static partial nint LoadIcon(nint instance, nint name);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW")]
    internal static partial nint LoadImage(nint instance, nint name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    internal static partial nint SetCapture(nint window);

    [LibraryImport("user32.dll")]
    internal static partial nint GetCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    [LibraryImport("uxtheme.dll")]
    internal static partial int BufferedPaintInit();

    [LibraryImport("uxtheme.dll")]
    internal static partial int BufferedPaintUnInit();

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PaintParameters(uint Size, uint Flags, nint Exclude, nint Blend);

    [LibraryImport("uxtheme.dll")]
    internal static partial nint BeginBufferedPaint(nint target, in NativeWindowRect rect, int format,
        in PaintParameters parameters, out nint bufferDc);

    [LibraryImport("uxtheme.dll")]
    internal static partial int BufferedPaintClear(nint buffer, nint rect);

    [LibraryImport("uxtheme.dll")]
    internal static partial int GetBufferedPaintBits(nint buffer, out nint pixels, out int stride);

    [LibraryImport("uxtheme.dll")]
    internal static partial int EndBufferedPaint(nint buffer, [MarshalAs(UnmanagedType.Bool)] bool update);
}
