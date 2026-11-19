using System.Runtime.InteropServices;
using Tessel.Core.Models;
using Tessel.Platform.Windows.Interop.DwmApi;
using Tessel.Platform.Windows.Interop.User32;
using Tessel.Platform.Windows.Interop.Kernel32;

namespace Tessel.Platform.Windows.Windows;

public static class NativeTopBar
{
    private const int BarHeight = 40;
    private const int WorkspaceCount = 9;

    private const uint WM_PAINT = 0x000F;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_SETCURSOR = 0x0020;
    private const uint WM_ERASEBKGND = 0x0014;

    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_TOPMOST = 0x00000008;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private const int IDC_ARROW = 32512;

    private const string ClassName = "TesselNativeTopBar";

    private static readonly nint HwndTopmost = new(-1);

    private static bool _classRegistered;

    private static readonly Dictionary<nint, BarState> Bars = [];

    public static event Action<nint, int>? WorkspaceSelected;
    public static event Action? LayoutInvalidated
    {
        add => NativeChrome.LayoutInvalidated += value;
        remove => NativeChrome.LayoutInvalidated -= value;
    }

    private sealed class BarState(nint hwnd)
    {
        public readonly ComposedBackdrop Backdrop = new(hwnd);
        public nint MonitorHandle;
        public int Width;
        public uint Dpi;
        public uint PendingDpi;
        public int HoveredWorkspace;
        public int X;
        public int Y;
        public int WorkspaceNumber;
        public bool IsActiveMonitor;
        public ContainerOrientation? SplitDirection;
        public bool IsPendingSplit;
        public (uint? Light, uint? Dark) IndicatorColors;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam
    );

    private static readonly WindowProcedure Procedure = WndProc;

    public static bool IsInternalWindow(nint handle)
    {
        return handle != 0 && (Bars.ContainsKey(handle) ||
            Bars.Values.Any(bar => bar.Backdrop.IsContentWindow(handle)));
    }

    public static uint GetPendingDpi(nint monitorHandle) =>
        Bars.Values.FirstOrDefault(bar => bar.MonitorHandle == monitorHandle)?.PendingDpi ?? 0;

    public static void UpdateActiveMonitor(nint monitorHandle)
    {
        foreach (var (window, state) in Bars)
        {
            bool active = state.MonitorHandle == monitorHandle;
            if (state.IsActiveMonitor == active)
                continue;

            state.IsActiveMonitor = active;
            WindowApi.InvalidateRect(window, nint.Zero, false);
        }
    }

    public static int Show(
        nint monitorHandle,
        LayoutArea area,
        int workspaceNumber,
        bool isActiveMonitor,
        ContainerOrientation? splitDirection,
        bool isPendingSplit,
        uint dpi = 0,
        (uint? Light, uint? Dark) indicatorColors = default
    )
    {
        if (area.Width <= 0)
            return 0;

        EnsureWindowClass();

        nint hwnd = Bars
            .FirstOrDefault(pair =>
                pair.Value.MonitorHandle == monitorHandle)
            .Key;

        if (hwnd == nint.Zero)
        {
            hwnd = WindowClassApi.CreateWindowEx(
                WS_EX_TOOLWINDOW | WS_EX_TOPMOST | NativeChrome.NoActivateStyle,
                ClassName,
                "Tessel",
                NativeBackdrop.FramedWindowStyle,
                area.X,
                area.Y,
                area.Width,
                BarHeight,
                nint.Zero,
                nint.Zero,
                ModuleApi.GetModuleHandle(null),
                nint.Zero
            );

            if (hwnd == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"Could not create topbar. Win32 error: " +
                    $"{Marshal.GetLastPInvokeError()}"
                );
            }

            Bars[hwnd] = new BarState(hwnd);
            Bars[hwnd].Backdrop.Refresh();
        }

        var state = Bars[hwnd];
        if (dpi == 0)
            dpi = state.PendingDpi != 0 ? state.PendingDpi : MonitorEnumerator.GetDpi(monitorHandle);
        state.PendingDpi = 0;
        bool moved = state.Dpi != dpi || state.X != area.X || state.Y != area.Y || state.Width != area.Width;
        bool repaint = moved || state.WorkspaceNumber != workspaceNumber ||
            state.IsActiveMonitor != isActiveMonitor || state.SplitDirection != splitDirection ||
            state.IsPendingSplit != isPendingSplit || state.IndicatorColors != indicatorColors;

        state.MonitorHandle = monitorHandle;
        state.X = area.X;
        state.Y = area.Y;
        state.Width = area.Width;
        state.WorkspaceNumber = workspaceNumber;
        state.IsActiveMonitor = isActiveMonitor;
        state.SplitDirection = splitDirection;
        state.IsPendingSplit = isPendingSplit;
        state.IndicatorColors = indicatorColors;

        state.Dpi = dpi;
        if (moved || !WindowApi.IsWindowVisible(hwnd))
            Position(hwnd, area, state.Dpi);

        if (repaint)
            WindowApi.InvalidateRect(hwnd, nint.Zero, false);
        return NativeChrome.Scale(BarHeight, state.Dpi);
    }

    public static void Hide(nint monitorHandle)
    {
        foreach (var (window, state) in Bars)
        {
            if (state.MonitorHandle != monitorHandle)
                continue;

            state.HoveredWorkspace = 0;
            WindowApi.ShowWindow(window, 0);
            return;
        }
    }

    private static void Position(nint hwnd, LayoutArea area, uint dpi)
    {
        WindowApi.SetWindowPos(
            hwnd, HwndTopmost, area.X, area.Y, area.Width,
            NativeChrome.Scale(BarHeight, dpi), SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private static void EnsureWindowClass()
    {
        if (_classRegistered)
            return;

        nint procedurePointer =
            Marshal.GetFunctionPointerForDelegate(Procedure);

        NativeWindowClassRegistrar.Register(
            ClassName,
            procedurePointer
        );

        _classRegistered = true;
    }

    private static nint WndProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam)
    {
        if (NativeBackdrop.HandleFrameMessage(message, out nint frameResult))
            return frameResult;

        switch (message)
        {
            case WM_PAINT:
                if (Bars.TryGetValue(hwnd, out var paintState))
                {
                    paintState.Backdrop.Paint(paintState.Dpi, paintState.Width,
                        NativeChrome.Scale(BarHeight, paintState.Dpi), (dc, colors) => PaintBar(dc, paintState, colors));
                    return nint.Zero;
                }
                break;

            case WM_ERASEBKGND:
                return 1;

            case NativeChrome.ThemeChanged:
            case NativeChrome.ColorizationChanged:
            case NativeChrome.SystemColorsChanged:
            case NativeChrome.SettingsChanged:
                NativeChrome.ThemeChangedFor(hwnd);
                if (Bars.TryGetValue(hwnd, out var themeState))
                {
                    themeState.Backdrop.Refresh();
                    if (message == NativeChrome.SettingsChanged)
                        NativeChrome.QueueLayoutRefresh(hwnd);
                }
                return nint.Zero;

            case NativeBackdrop.CompositionChanged:
                if (Bars.TryGetValue(hwnd, out var compositionState))
                    compositionState.Backdrop.Refresh();
                WindowApi.InvalidateRect(hwnd, nint.Zero, false);
                return nint.Zero;

            case NativeChrome.DpiChanged:
                if (Bars.TryGetValue(hwnd, out var dpiState))
                {
                    dpiState.PendingDpi = NativeChrome.DpiFromMessage(wParam);
                    NativeChrome.QueueLayoutRefresh(hwnd);
                }
                return nint.Zero;

            case NativeChrome.RefreshLayout:
                if (Bars.TryGetValue(hwnd, out var bar) &&
                    bar.Dpi != (bar.PendingDpi != 0 ? bar.PendingDpi : MonitorEnumerator.GetDpi(bar.MonitorHandle)))
                {
                    NativeChrome.RequestLayout();
                }
                return nint.Zero;

            case NativeChrome.MouseMove:
                if (Bars.TryGetValue(hwnd, out var hoverState))
                {
                    int workspace = WorkspaceAt(hoverState, lParam);
                    if (workspace != hoverState.HoveredWorkspace)
                    {
                        if (workspace != 0 && hoverState.HoveredWorkspace == 0)
                            NativeChrome.TrackMouseLeave(hwnd);
                        hoverState.HoveredWorkspace = workspace;
                        WindowApi.InvalidateRect(hwnd, nint.Zero, false);
                    }
                }
                return nint.Zero;

            case NativeChrome.MouseLeave:
                if (Bars.TryGetValue(hwnd, out var leaveState) && leaveState.HoveredWorkspace != 0)
                {
                    leaveState.HoveredWorkspace = 0;
                    WindowApi.InvalidateRect(hwnd, nint.Zero, false);
                }
                return nint.Zero;

            case WM_LBUTTONUP:
                if (Bars.TryGetValue(hwnd, out var state) && WorkspaceAt(state, lParam) is > 0 and var number)
                    WorkspaceSelected?.Invoke(state.MonitorHandle, number);
                return nint.Zero;

            case WM_SETCURSOR:
                WindowApi.SetCursor(
                    WindowApi.LoadCursor(
                        nint.Zero,
                        (nint)IDC_ARROW
                    )
                );

                return 1;
        }

        return WindowClassApi.DefWindowProc(
            hwnd,
            message,
            wParam,
            lParam
        );
    }

    public static void RemoveBarsNotOnMonitors(
        IReadOnlySet<nint> activeMonitorHandles)
    {
        ArgumentNullException.ThrowIfNull(activeMonitorHandles);

        foreach (var pair in Bars.ToArray())
        {
            if (activeMonitorHandles.Contains(pair.Value.MonitorHandle))
                continue;

            if (!WindowApi.DestroyWindow(pair.Key))
            {
                Console.Error.WriteLine(
                    $"Could not destroy obsolete top bar. " +
                    $"Win32 error: {Marshal.GetLastPInvokeError()}"
                );

                continue;
            }

            pair.Value.Backdrop.Dispose();
            Bars.Remove(pair.Key);
        }
    }

    public static void ShutDownComposition()
    {
        DirectChromePaint.ShutDown();
        HostBackdropVisual.ShutdownDispatcher();
    }

    private static NativeWindowRect WorkspaceBounds(BarState state, int number)
    {
        uint dpi = state.Dpi;
        int inset = NativeChrome.Scale(8, dpi);
        int stride = Math.Min(NativeChrome.Scale(36, dpi), (state.Width - inset * 2) / WorkspaceCount);
        int gap = NativeChrome.Scale(4, dpi);
        int left = inset + (number - 1) * stride;
        return NativeChrome.Rect(left, NativeChrome.Scale(5, dpi),
            left + Math.Max(0, stride - gap), NativeChrome.Scale(BarHeight, dpi) - NativeChrome.Scale(5, dpi));
    }

    private static int WorkspaceAt(BarState state, nint mousePosition)
    {
        for (int number = 1; number <= WorkspaceCount; number++)
        {
            if (NativeChrome.Contains(WorkspaceBounds(state, number), mousePosition))
                return number;
        }

        return 0;
    }

    private static void PaintBar(nint hdc, BarState state, NativeChrome.Palette colors)
    {
        uint dpi = state.Dpi;
        int height = NativeChrome.Scale(BarHeight, dpi);
        int line = NativeChrome.Scale(1, dpi);

        NativeChrome.Fill(hdc, NativeChrome.Rect(0, height - line, state.Width, height), colors.Divider);

        for (int number = 1; number <= WorkspaceCount; number++)
        {
            var rect = WorkspaceBounds(state, number);
            bool hovered = number == state.HoveredWorkspace;
            if (hovered)
                NativeChrome.FillRounded(hdc, rect, colors.HighContrast ? colors.Selected : colors.Hover,
                    colors.HighContrast ? 0 : NativeChrome.Scale(6, dpi));
            uint numberColor = hovered && colors.HighContrast ? colors.SelectedText : colors.Text;
            NativeChrome.Text(hdc, number.ToString(), rect, numberColor, NativeChrome.Center);

            if (number == state.WorkspaceNumber)
            {
                int center = (rect.Left + rect.Right) / 2;
                int halfWidth = Math.Min(NativeChrome.Scale(state.IsActiveMonitor ? 8 : 4, dpi),
                    (rect.Right - rect.Left) / 2);
                int thickness = NativeChrome.Scale(3, dpi);
                var indicator = NativeChrome.Rect(center - halfWidth, rect.Bottom - thickness,
                    center + halfWidth, rect.Bottom);
                uint color = colors.HighContrast ? numberColor :
                    state.IsActiveMonitor ? colors.Indicator(state.IndicatorColors) : colors.SecondaryText;
                NativeChrome.FillRounded(hdc, indicator, color, colors.HighContrast ? 0 : thickness / 2.0);
            }
        }

        DrawStatus(hdc, state, height, colors);
    }

    private static void DrawStatus(nint hdc, BarState state, int height, NativeChrome.Palette colors)
    {
        if (state.SplitDirection is not { } direction)
            return;

        uint dpi = state.Dpi;
        int left = WorkspaceBounds(state, WorkspaceCount).Right + NativeChrome.Scale(12, dpi);
        int right = state.Width - NativeChrome.Scale(14, dpi);
        int available = right - left;
        int iconSize = NativeChrome.Scale(16, dpi);
        int dotSize = NativeChrome.Scale(4, dpi);
        int dotGap = NativeChrome.Scale(5, dpi);
        if (available < iconSize)
            return;

        bool showPendingDot = state.IsPendingSplit && available >= iconSize + dotGap + dotSize;
        if (showPendingDot)
        {
            int dotRight = right - iconSize - dotGap;
            int dotTop = (height - dotSize) / 2;
            NativeChrome.FillRounded(hdc, NativeChrome.Rect(dotRight - dotSize, dotTop, dotRight, dotTop + dotSize),
                colors.HighContrast ? colors.Text : colors.Accent, dotSize / 2.0);
        }

        var icon = NativeChrome.Rect(right - iconSize, (height - iconSize) / 2,
            right, (height - iconSize) / 2 + iconSize);
        var first = icon;
        var second = icon;
        int divider = NativeChrome.Scale(2, dpi);
        if (direction == ContainerOrientation.Horizontal)
        {
            first.Right = icon.Left + (iconSize - divider) / 2;
            second.Left = first.Right + divider;
        }
        else
        {
            first.Bottom = icon.Top + (iconSize - divider) / 2;
            second.Top = first.Bottom + divider;
        }

        double radius = colors.HighContrast ? 0 : NativeChrome.Scale(2, dpi);
        uint iconColor = state.IsPendingSplit && !showPendingDot && !colors.HighContrast
            ? colors.Accent : colors.SecondaryText;
        NativeChrome.FillRounded(hdc, first, iconColor, radius);
        NativeChrome.FillRounded(hdc, second, iconColor, radius);
    }
}
