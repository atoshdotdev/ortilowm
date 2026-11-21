using System.Runtime.InteropServices;

namespace Ortilo.Platform.Windows.Interop.User32;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativePaintStruct
{
    public nint Hdc;
    public int Erase;
    public NativeWindowRect Paint;
    public int Restore;
    public int IncUpdate;
    public fixed byte Reserved[32];
}
