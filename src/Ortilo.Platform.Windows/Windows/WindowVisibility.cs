using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

public static class WindowVisibility
{
    public static bool Exists(nint handle) => WindowApi.IsWindow(handle);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_MINIMIZE = 6;
    private const int SW_SHOWMINNOACTIVE = 7;
    private const int SW_SHOWNA = 8;
    private const string HiddenProperty = "Ortilo.HiddenWindow.{79B868DE-9A78-4EAF-B20D-64D0A71A7979}";
    private static readonly nint HiddenMarker = 1;
    private static readonly HashSet<nint> LayoutHiddenWindows = [];

    public static bool IsHiddenForLayout(nint handle) => LayoutHiddenWindows.Contains(handle);

    public static void ForgetLayoutHiddenWindow(nint handle) => LayoutHiddenWindows.Remove(handle);

    public static void HideForLayout(nint handle)
    {
        if (!WindowApi.IsWindowVisible(handle) || WindowApi.IsIconic(handle))
            return;

        if (Hide(handle))
            LayoutHiddenWindows.Add(handle);
    }

    public static bool Hide(nint handle)
    {
        if (!WindowApi.IsWindowVisible(handle) || WindowApi.IsIconic(handle))
            return false;

        // Set the marker before hiding. It belongs to this HWND and remains
        // available if Ortilo exits before it can restore the window.
        if (!WindowApi.SetProp(handle, HiddenProperty, HiddenMarker))
        {
            Console.Error.WriteLine($"Could not mark window 0x{handle:X} for recovery. Win32 error: {Marshal.GetLastPInvokeError()}");
            return false;
        }

        WindowApi.ShowWindow(handle, SW_HIDE);
        if (!WindowApi.IsWindowVisible(handle))
            return true;

        WindowApi.RemoveProp(handle, HiddenProperty);
        return false;
    }

    public static bool Show(nint handle)
    {
        if (!WindowApi.IsWindow(handle))
            return false;

        WindowApi.ShowWindow(handle, SW_SHOW);
        ClearMarkerIfVisible(handle);
        return true;
    }

    public static bool ShowWithoutActivate(nint handle)
    {
        if (!WindowApi.IsWindow(handle))
            return false;

        WindowApi.ShowWindow(handle, SW_SHOWNOACTIVATE);
        if (ClearMarkerIfVisible(handle))
            LayoutHiddenWindows.Remove(handle);
        return true;
    }

    private static bool ClearMarkerIfVisible(nint handle)
    {
        if (!WindowApi.IsWindowVisible(handle))
            return false;

        if (WindowApi.GetProp(handle, HiddenProperty) == HiddenMarker)
            WindowApi.RemoveProp(handle, HiddenProperty);
        return true;
    }

    public static unsafe void RestoreHiddenWindows()
    {
        if (WindowEnumerationApi.EnumWindows(&RestoreHiddenWindow, 0) == 0)
            Console.Error.WriteLine($"Could not enumerate hidden windows for recovery. Win32 error: {Marshal.GetLastPInvokeError()}");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int RestoreHiddenWindow(nint handle, nint state)
    {
        if (WindowApi.GetProp(handle, HiddenProperty) != HiddenMarker)
            return 1;

        if (!WindowApi.IsWindowVisible(handle))
        {
            WindowApi.ShowWindow(handle, WindowApi.IsIconic(handle) ? SW_SHOWMINNOACTIVE : SW_SHOWNA);
            if (!WindowApi.IsWindowVisible(handle))
            {
                const uint showInPlace = 0x0040 | 0x0010 | 0x0004 | 0x0002 | 0x0001;
                WindowApi.SetWindowPos(handle, 0, 0, 0, 0, 0, showInPlace);
            }
        }

        if (ClearMarkerIfVisible(handle))
            LayoutHiddenWindows.Remove(handle);

        return 1;
    }

    public static bool Minimize(nint handle)
    {
        if (!WindowApi.IsWindow(handle))
            return false;

        WindowApi.ShowWindow(handle, SW_MINIMIZE);
        return true;
    }

    public static bool IsMinimized(nint handle)
    {
        if (!WindowApi.IsWindow(handle))
            return false;

        return WindowApi.IsIconic(handle);
    }
}
