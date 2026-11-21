using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Interop.User32;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWindowRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}
