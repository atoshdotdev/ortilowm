using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Ortilo.Core.Models;
using Ortilo.Platform.Windows.Interop.DwmApi;
using Ortilo.Platform.Windows.Interop.Kernel32;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

public static class WindowEnumerator
{
    private const uint GW_OWNER = 4;

    private const int GWL_EXSTYLE = -20;

    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private const uint DWMWA_CLOAKED = 14;

    private sealed class EnumerationContext
    {
        public List<WindowMetadata> Windows { get; } = [];
        private readonly Dictionary<uint, bool> _higherIntegrityByProcess = [];

        public Exception? Exception { get; set; }

        public bool HasHigherIntegrity(uint processId)
        {
            if (!_higherIntegrityByProcess.TryGetValue(processId, out bool higher))
            {
                higher = ProcessIntegrity.Current is uint current &&
                    ProcessIntegrity.ReadLevel(processId) is uint target && target > current;
                _higherIntegrityByProcess.Add(processId, higher);
            }

            return higher;
        }
    }

    [UnmanagedCallersOnly(
        CallConvs = [typeof(CallConvStdcall)]
    )]
    private static int EnumWindowsCallback(
        nint hWnd,
        nint lParam
    )
    {
        EnumerationContext? context = null;

        try
        {
            var handle = GCHandle.FromIntPtr(lParam);

            context = (EnumerationContext)handle.Target!;

            AddWindow(context, hWnd);

            return 1;
        }
        catch (Exception exception)
        {
            if (context is not null)
                context.Exception = exception;

            return 0;
        }
    }

    public static unsafe IReadOnlyList<WindowMetadata> GetTopLevelWindows()
    {
        var context = new EnumerationContext();

        GCHandle handle = GCHandle.Alloc(context);

        try
        {
            int result = WindowEnumerationApi.EnumWindows(
                &EnumWindowsCallback,
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
                    "Failed to enumerate top-level windows."
                );
            }

            return context.Windows;
        }
        finally
        {
            handle.Free();
        }
    }

    private static void AddWindow(
        EnumerationContext context,
        nint hWnd
    )
    {
        if (WindowEnumerationApi.IsWindowVisible(hWnd) == 0 && !WindowVisibility.IsHiddenForLayout(hWnd))
            return;

        int length = WindowEnumerationApi.GetWindowTextLength(hWnd);

        if (length == 0)
            return;

        var buffer = new char[length + 1];

        int charsCopied;

        unsafe
        {
            fixed (char* text = buffer)
            {
                charsCopied = WindowEnumerationApi.GetWindowText(
                    hWnd,
                    text,
                    buffer.Length
                );
            }
        }

        if (charsCopied == 0)
            return;

        WindowEnumerationApi.GetWindowThreadProcessId(
            hWnd,
            out uint processId
        );

        bool hasOwner =
            WindowEnumerationApi.GetWindow(hWnd, GW_OWNER) != nint.Zero;

        uint extendedStyle = unchecked(
            (uint)WindowEnumerationApi.GetWindowLong(
                hWnd,
                GWL_EXSTYLE
            )
        );

        bool isToolWindow =
            (extendedStyle & WS_EX_TOOLWINDOW) != 0;
        bool isCloaked = IsCloaked(hWnd);

        context.Windows.Add(new WindowMetadata(
            hWnd,
            new string(buffer, 0, charsCopied),
            processId,
            hasOwner,
            isToolWindow,
            isCloaked,
            WindowEnumerationApi.IsIconic(hWnd) != 0,
            HasHigherIntegrity: !isToolWindow && !isCloaked && context.HasHigherIntegrity(processId)
        ));
    }

    private static bool IsCloaked(nint hWnd)
    {
        int result = DwmWindowApi.DwmGetWindowAttribute(
            hWnd,
            DWMWA_CLOAKED,
            out int cloaked,
            sizeof(int)
        );

        return result == 0 && cloaked != 0;
    }
}
