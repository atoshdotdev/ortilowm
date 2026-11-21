using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Interop.User32;

internal static partial class LayeredWindowApi
{
    internal static bool Present(nint window, nint sourceDc, int width, int height) =>
        UpdateLayeredWindow(window, 0, 0, new(width, height), sourceDc, new(0, 0), 0, new(0, 0, 255, 1), 2);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct BlendFunction(byte Operation, byte Flags, byte Opacity, byte AlphaFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(nint window, nint destinationDc, nint destination,
        in Point size, nint sourceDc, in Point source, uint colorKey, in BlendFunction blend, uint flags);
}
