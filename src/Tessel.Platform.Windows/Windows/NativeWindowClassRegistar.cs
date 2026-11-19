using System.ComponentModel;
using System.Runtime.InteropServices;
using Tessel.Platform.Windows.Interop.Kernel32;
using Tessel.Platform.Windows.Interop.User32;

namespace Tessel.Platform.Windows.Windows;

internal static class NativeWindowClassRegistrar
{
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;

    public static void Register(
        string className,
        nint windowProcedure,
        nint background = default)
    {
        nint classNamePointer =
            Marshal.StringToHGlobalUni(className);

        try
        {
            var windowClass = new NativeWindowClass
            {
                Size = (uint)Marshal.SizeOf<NativeWindowClass>(),
                WindowProcedure = windowProcedure,
                Instance = ModuleApi.GetModuleHandle(null),
                Background = background,
                ClassName = classNamePointer
            };

            if (WindowClassApi.RegisterClassEx(ref windowClass) != 0)
                return;

            int error = Marshal.GetLastPInvokeError();

            if (error == ERROR_CLASS_ALREADY_EXISTS)
                return;

            throw new Win32Exception(
                error,
                $"Could not register native window class '{className}'.");
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePointer);
        }
    }
}
