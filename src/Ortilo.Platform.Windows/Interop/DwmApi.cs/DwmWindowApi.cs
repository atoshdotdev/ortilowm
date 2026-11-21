using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Interop.DwmApi;

internal static partial class DwmWindowApi
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct FrameMargins(int Left, int Right, int Top, int Bottom);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmExtendFrameIntoClientArea(nint hwnd, in FrameMargins margins);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetColorizationColor(out uint argb, out int opaqueBlend);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(
        nint hWnd, uint attribute, out NativeWindowRect value, int valueSize);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(
        nint hWnd,
        uint attribute,
        out int value,
        int valueSize
    );

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmSetWindowAttribute(
        nint hWnd,
        uint attribute,
        in int attributeValue,
        uint attributeSize
    );
}
