using Ortilo.Platform.Windows.Interop.User32;
using Ortilo.Platform.Windows.Interop.Kernel32;

namespace Ortilo.Platform.Windows.Windows;

public static class WindowFocuser
{
    private const int SW_RESTORE = 9;

    public static bool TryFocus(nint handle)
    {
        if (handle == nint.Zero ||
            !WindowApi.IsWindow(handle))
        {
            return false;
        }

        if (WindowApi.IsIconic(handle))
            WindowApi.ShowWindow(handle, SW_RESTORE);
        else if (!WindowApi.IsWindowVisible(handle))
            WindowApi.ShowWindow(handle, SW_RESTORE);

        nint foregroundHandle = WindowApi.GetForegroundWindow();

        if (foregroundHandle == handle)
            return true;

        uint currentThreadId = ThreadApi.GetCurrentThreadId();

        uint foregroundThreadId =
            foregroundHandle != nint.Zero
                ? WindowApi.GetWindowThreadProcessId(
                    foregroundHandle,
                    out _)
                : 0;

        uint targetThreadId =
            WindowApi.GetWindowThreadProcessId(handle, out _);

        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            if (foregroundThreadId != 0 &&
                foregroundThreadId != currentThreadId)
            {
                attachedToForeground = WindowApi.AttachThreadInput(
                    currentThreadId,
                    foregroundThreadId,
                    true
                );
            }

            if (targetThreadId != 0 &&
                targetThreadId != currentThreadId &&
                targetThreadId != foregroundThreadId)
            {
                attachedToTarget = WindowApi.AttachThreadInput(
                    currentThreadId,
                    targetThreadId,
                    true
                );
            }

            WindowApi.SetForegroundWindow(handle);

            return WindowApi.GetForegroundWindow() == handle;
        }
        finally
        {
            if (attachedToTarget)
            {
                WindowApi.AttachThreadInput(
                    currentThreadId,
                    targetThreadId,
                    false
                );
            }

            if (attachedToForeground)
            {
                WindowApi.AttachThreadInput(
                    currentThreadId,
                    foregroundThreadId,
                    false
                );
            }
        }
    }

    public static nint GetForegroundHandle()
    {
        return WindowApi.GetForegroundWindow();
    }

    public static bool IsVisible(nint handle)
    {
        return handle != nint.Zero &&
               WindowApi.IsWindowVisible(handle);
    }
}
