using System.Runtime.InteropServices;
using Ortilo.Core.Models;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

public static class WindowPositioner
{
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    public static LayoutArea? GetWindowBounds(nint handle) =>
        WindowApi.GetWindowRect(handle, out var bounds)
            ? new LayoutArea(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)
            : null;

    public static (int X, int Y)? GetCursorPosition() =>
        WindowApi.GetCursorPos(out var point) ? (point.X, point.Y) : null;

    public static bool TryApply(WindowPlacement placement, out int win32Error)
    {
        win32Error = 0;
        if (placement.Width <= 0 || placement.Height <= 0)
            return false;

        if (!WindowApi.IsWindow(placement.Handle))
            return false;

        if (!WindowApi.IsWindowVisible(placement.Handle))
            return false;

        if (WindowApi.IsIconic(placement.Handle))
            return false;

        if (WindowApi.IsZoomed(placement.Handle))
        {
            WindowVisibility.ShowWithoutActivate(placement.Handle);
            if (WindowApi.IsZoomed(placement.Handle))
                return false;
        }

        uint previousDpi = WindowDpi.Get(placement.Handle);

        if (!SetPosition(placement, out win32Error))
            return false;

        uint currentDpi = WindowDpi.Get(placement.Handle);

        if (previousDpi != currentDpi)
            return SetPosition(placement, out win32Error);

        return true;
    }

    private static bool SetPosition(WindowPlacement placement, out int win32Error)
    {
        win32Error = 0;
        int x = placement.X;
        int y = placement.Y;
        int width = placement.Width;
        int height = placement.Height;
        if (WindowApi.GetWindowRect(placement.Handle, out var outer))
        {
            if (WindowFrame.TryGetVisibleBounds(placement.Handle, out var visible))
            {
                // Layout coordinates describe the visible frame. SetWindowPos takes the outer HWND bounds.
                int left = visible.Left - outer.Left;
                int top = visible.Top - outer.Top;
                int right = outer.Right - visible.Right;
                int bottom = outer.Bottom - visible.Bottom;
                if (width + left + right > 0 && height + top + bottom > 0)
                {
                    x -= left;
                    y -= top;
                    width += left + right;
                    height += top + bottom;
                }
            }

            if (x == outer.Left && y == outer.Top &&
                width == outer.Right - outer.Left && height == outer.Bottom - outer.Top)
                return true;
        }

        if (WindowApi.SetWindowPos(
            placement.Handle,
            nint.Zero,
            x,
            y,
            width,
            height,
            SWP_NOZORDER | SWP_NOACTIVATE
        ))
            return true;

        win32Error = Marshal.GetLastPInvokeError();
        return false;
    }
}
