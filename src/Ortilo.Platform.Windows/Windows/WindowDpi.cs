using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

public static class WindowDpi
{
    public static uint Get(nint handle)
    {
        if (handle == nint.Zero ||
            !WindowApi.IsWindow(handle))
        {
            return 0;
        }

        return WindowApi.GetDpiForWindow(handle);
    }
}
