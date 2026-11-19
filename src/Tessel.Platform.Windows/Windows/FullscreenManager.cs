using System.Runtime.InteropServices;
using Tessel.Core.Models;
using Tessel.Platform.Windows.Interop.User32;

namespace Tessel.Platform.Windows.Windows;

public static class FullscreenManager
{
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const long WS_OVERLAPPEDWINDOW = 0x00CF0000L;
    private const long WS_POPUP = unchecked((long)0x80000000);

    private const long WS_EX_DLGMODALFRAME = 0x00000001L;
    private const long WS_EX_WINDOWEDGE = 0x00000100L;
    private const long WS_EX_CLIENTEDGE = 0x00000200L;
    private const long WS_EX_STATICEDGE = 0x00020000L;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private sealed record FullscreenState(
        NativeWindowRect OriginalRect,
        nint OriginalStyle,
        nint OriginalExtendedStyle,
        nint FullscreenStyle,
        nint FullscreenExtendedStyle,
        LayoutArea MonitorBounds
    );

    private static readonly Dictionary<nint, FullscreenState> States = [];

    public static bool IsFullscreen(nint handle)
    {
        if (!States.ContainsKey(handle))
            return false;

        if (WindowApi.IsWindow(handle))
            return true;

        States.Remove(handle);
        return false;
    }

    public static bool Toggle(nint handle)
    {
        if (States.ContainsKey(handle))
            return Restore(handle);

        return Enter(handle);
    }

    private static bool Enter(nint handle)
    {
        if (!WindowApi.IsWindow(handle))
            return false;

        if (!WindowApi.GetWindowRect(handle, out NativeWindowRect originalRect))
            return false;

        var monitors = MonitorEnumerator.GetMonitors();

        if (monitors.Count == 0)
            return false;

        int centerX = originalRect.Left +
            (originalRect.Right - originalRect.Left) / 2;

        int centerY = originalRect.Top +
            (originalRect.Bottom - originalRect.Top) / 2;

        var monitor = monitors.FirstOrDefault(m =>
            centerX >= m.Bounds.X &&
            centerX < m.Bounds.X + m.Bounds.Width &&
            centerY >= m.Bounds.Y &&
            centerY < m.Bounds.Y + m.Bounds.Height
        ) ?? monitors.FirstOrDefault(m => m.IsPrimary);

        if (monitor is null)
            return false;

        if (!TryGetWindowLong(handle, GWL_STYLE, out nint originalStyle))
            return false;

        if (!TryGetWindowLong(handle, GWL_EXSTYLE, out nint originalExtendedStyle))
            return false;

        nint fullscreenStyle = unchecked(
            (nint)(((long)originalStyle & ~WS_OVERLAPPEDWINDOW) | WS_POPUP)
        );

        long extendedStyle = (long)originalExtendedStyle;

        extendedStyle &= ~(
            WS_EX_DLGMODALFRAME |
            WS_EX_WINDOWEDGE |
            WS_EX_CLIENTEDGE |
            WS_EX_STATICEDGE
        );

        nint fullscreenExtendedStyle = unchecked((nint)extendedStyle);

        var state = new FullscreenState(
            originalRect,
            originalStyle,
            originalExtendedStyle,
            fullscreenStyle,
            fullscreenExtendedStyle,
            monitor.Bounds
        );

        if (!TrySetWindowLong(handle, GWL_STYLE, fullscreenStyle))
            return false;

        if (!TrySetWindowLong(handle, GWL_EXSTYLE, fullscreenExtendedStyle))
        {
            TrySetWindowLong(handle, GWL_STYLE, originalStyle);
            return false;
        }

        if (!ApplyBounds(handle, monitor.Bounds))
        {
            RollbackEnter(handle, state);
            return false;
        }

        States[handle] = state;

        return true;
    }

    private static bool Restore(nint handle)
    {
        if (!States.TryGetValue(handle, out var state))
            return false;

        if (!WindowApi.IsWindow(handle))
        {
            States.Remove(handle);
            return false;
        }

        if (!TrySetWindowLong(handle, GWL_STYLE, state.OriginalStyle))
            return false;

        if (!TrySetWindowLong(handle, GWL_EXSTYLE, state.OriginalExtendedStyle))
        {
            TrySetWindowLong(handle, GWL_STYLE, state.FullscreenStyle);
            return false;
        }

        int originalWidth = state.OriginalRect.Right - state.OriginalRect.Left;
        int originalHeight = state.OriginalRect.Bottom - state.OriginalRect.Top;

        bool success = ApplyBounds(
            handle,
            state.OriginalRect.Left,
            state.OriginalRect.Top,
            originalWidth,
            originalHeight
        );

        if (!success)
        {
            RollbackRestore(handle, state);
            return false;
        }

        States.Remove(handle);

        return true;
    }

    private static bool TryGetWindowLong(
        nint handle,
        int index,
        out nint value
    )
    {
        Marshal.SetLastPInvokeError(0);

        value = WindowApi.GetWindowLongPtr(handle, index);

        return value != nint.Zero ||
               Marshal.GetLastPInvokeError() == 0;
    }

    private static bool TrySetWindowLong(
        nint handle,
        int index,
        nint value
    )
    {
        Marshal.SetLastPInvokeError(0);

        nint previousValue = WindowApi.SetWindowLongPtr(
            handle,
            index,
            value
        );

        return previousValue != nint.Zero ||
               Marshal.GetLastPInvokeError() == 0;
    }

    private static bool ApplyBounds(
        nint handle,
        LayoutArea bounds
    )
    {
        return ApplyBounds(
            handle,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height
        );
    }

    private static bool ApplyBounds(
        nint handle,
        int x,
        int y,
        int width,
        int height
    )
    {
        return WindowApi.SetWindowPos(
            handle,
            nint.Zero,
            x,
            y,
            width,
            height,
            SWP_NOZORDER |
            SWP_NOACTIVATE |
            SWP_FRAMECHANGED
        );
    }

    private static void RollbackEnter(
        nint handle,
        FullscreenState state
    )
    {
        TrySetWindowLong(handle, GWL_STYLE, state.OriginalStyle);

        TrySetWindowLong(
            handle,
            GWL_EXSTYLE,
            state.OriginalExtendedStyle
        );

        WindowApi.SetWindowPos(
            handle,
            nint.Zero,
            state.OriginalRect.Left,
            state.OriginalRect.Top,
            state.OriginalRect.Right - state.OriginalRect.Left,
            state.OriginalRect.Bottom - state.OriginalRect.Top,
            SWP_NOZORDER |
            SWP_NOACTIVATE |
            SWP_FRAMECHANGED
        );
    }

    private static void RollbackRestore(
        nint handle,
        FullscreenState state
    )
    {
        TrySetWindowLong(handle, GWL_STYLE, state.FullscreenStyle);

        TrySetWindowLong(
            handle,
            GWL_EXSTYLE,
            state.FullscreenExtendedStyle
        );

        ApplyBounds(handle, state.MonitorBounds);
    }
}
