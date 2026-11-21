using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Interop.User32;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMessage
{
    public nint HWnd;
    public uint Message;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public NativePoint Point;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;
}
