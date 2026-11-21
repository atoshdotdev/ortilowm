using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Ortilo.Core.Models;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

public static class MonitorEnumerator
{
    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    private sealed class EnumerationContext
    {
        public List<DisplayMonitor> Monitors { get; } = [];

        public Exception? Exception { get; set; }
    }

    [UnmanagedCallersOnly(
        CallConvs = [typeof(CallConvStdcall)]
    )]
    private static int MonitorEnumCallback(
        nint hMonitor,
        nint hdcMonitor,
        nint monitorRect,
        nint data
    )
    {
        EnumerationContext? context = null;

        try
        {
            var handle = GCHandle.FromIntPtr(data);

            context = (EnumerationContext)handle.Target!;

            AddMonitor(context, hMonitor);

            return 1;
        }
        catch (Exception exception)
        {
            if (context is not null)
                context.Exception = exception;

            return 0;
        }
    }

    public static unsafe IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var context = new EnumerationContext();

        GCHandle handle = GCHandle.Alloc(context);

        try
        {
            int result = MonitorEnumerationApi.EnumDisplayMonitors(
                nint.Zero,
                nint.Zero,
                &MonitorEnumCallback,
                GCHandle.ToIntPtr(handle)
            );

            if (context.Exception is not null)
            {
                ExceptionDispatchInfo
                    .Capture(context.Exception)
                    .Throw();
            }

            if (result == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Failed to enumerate display monitors."
                );
            }

            return context.Monitors;
        }
        finally
        {
            handle.Free();
        }
    }

    private static unsafe void AddMonitor(
        EnumerationContext context,
        nint hMonitor
    )
    {
        var info = new NativeMonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMonitorInfo>()
        };

        if (!MonitorEnumerationApi.GetMonitorInfo(hMonitor, ref info))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"Failed to retrieve information for monitor 0x{hMonitor:X}."
            );
        }

        char* name = info.DeviceName;
        string deviceName = new string(name).TrimEnd('\0');

        var bounds = new LayoutArea(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top
        );

        var workArea = new LayoutArea(
            info.Work.Left,
            info.Work.Top,
            info.Work.Right - info.Work.Left,
            info.Work.Bottom - info.Work.Top
        );

        context.Monitors.Add(new DisplayMonitor(
            hMonitor,
            deviceName,
            bounds,
            workArea,
            (info.Flags & MONITORINFOF_PRIMARY) != 0
        ));
    }

    public static nint GetMonitorForWindow(nint hwnd)
    {
        if (hwnd == nint.Zero)
            return nint.Zero;

        return MonitorEnumerationApi.MonitorFromWindow(
            hwnd,
            MONITOR_DEFAULTTONEAREST
        );
    }

    public static uint GetDpi(nint monitor) =>
        MonitorEnumerationApi.GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi != 0 ? dpi : 96;
}
