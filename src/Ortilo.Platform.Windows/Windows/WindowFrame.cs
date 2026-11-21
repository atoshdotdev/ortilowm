using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.DwmApi;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

internal static class WindowFrame
{
    private const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    internal static bool TryGetVisibleBounds(nint handle, out NativeWindowRect bounds) =>
        DwmWindowApi.DwmGetWindowAttribute(
            handle, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf<NativeWindowRect>()) >= 0 &&
        bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
}
