using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.User32;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWindowClass
{
    public uint Size;
    public uint Style;
    public nint WindowProcedure;
    public int ClassExtra;
    public int WindowExtra;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint Background;
    public nint MenuName;
    public nint ClassName;
    public nint SmallIcon;
}
