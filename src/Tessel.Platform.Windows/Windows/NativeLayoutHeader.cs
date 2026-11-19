using System.Runtime.InteropServices;
using Tessel.Core.Models;
using Tessel.Platform.Windows.Interop.Gdi32;
using Tessel.Platform.Windows.Interop.User32;
using Tessel.Platform.Windows.Interop.Kernel32;

namespace Tessel.Platform.Windows.Windows;

public sealed class NativeLayoutHeader
{
    private const int RowHeight = 36;

    private int _headerHeight = RowHeight;
    private uint _dpi = 96;
    private uint _pendingDpi;
    private (int Width, int Height, int Radius, LayoutDockEdges Edges)? _cornerMask;
    private bool _hasWindowRegion;
    private LayoutDockEdges _dockedEdges;
    private bool _showDivider = true;
    private (uint? Light, uint? Dark) _indicatorColors;
    private int _hoveredIndex = -1;
    private bool _closeHovered;
    private nint _pressedCloseHandle;
    private nint _closeFont;
    private uint _closeFontDpi;

    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_SETCURSOR = 0x0020;

    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_TOPMOST = 0x00000008;
    private const int GWL_EXSTYLE = -20;
    private const uint WM_CLOSE = 0x0010;

    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private static nint _foregroundOverlay;

    private const int SW_HIDE = 0;

    private const int IDC_ARROW = 32512;

    private const string WindowClassName = "TesselLayoutHeader";

    private static readonly Dictionary<nint, NativeLayoutHeader> Headers = [];
    private static bool _classRegistered;

    private readonly nint _monitorHandle;
    private readonly Guid _containerId;
    private readonly nint _headerHandle;
    private readonly ComposedBackdrop _backdrop;
    private int _x;
    private int _y;
    private int _width;

    private IReadOnlyList<WindowMetadata> _windows = [];

    private nint _activeHandle;
    private LayoutType _layoutType;

    public static event Action<nint>? WindowSelected;

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
        return handle != 0 && (Headers.ContainsKey(handle) ||
            Headers.Values.Any(header => header._backdrop.IsContentWindow(handle)));
    }

    private static NativeLayoutHeader? FindHeader(nint monitorHandle, Guid containerId) =>
        Headers.Values.FirstOrDefault(header =>
            header._monitorHandle == monitorHandle && header._containerId == containerId);

    public static int GetRowHeight(uint dpi) => NativeChrome.Scale(RowHeight, dpi == 0 ? 96 : dpi);

    public static uint GetPendingDpi(nint monitorHandle) =>
        Headers.Values.FirstOrDefault(header => header._monitorHandle == monitorHandle && header._pendingDpi != 0)
            ?._pendingDpi ?? 0;

    public static int Show(
        nint monitorHandle,
        LayoutArea area,
        IReadOnlyList<WindowMetadata> windows,
        nint activeHandle,
        LayoutType layoutType,
        Guid containerId = default,
        int? reservedHeight = null,
        uint dpi = 0,
        LayoutDockEdges dockedEdges = LayoutDockEdges.None,
        bool showDivider = true,
        (uint? Light, uint? Dark) indicatorColors = default)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (area.Width <= 0 || area.Height <= 0 || windows.Count == 0)
        {
            Hide(monitorHandle, containerId);
            return 0;
        }

        var header = FindHeader(monitorHandle, containerId) ??
            new NativeLayoutHeader(monitorHandle, containerId, area);
        return header.Update(area, windows, activeHandle, layoutType, reservedHeight, dpi, dockedEdges, showDivider,
            indicatorColors);
    }

    private NativeLayoutHeader(nint monitorHandle, Guid containerId, LayoutArea area)
    {
        EnsureWindowClass();
        _monitorHandle = monitorHandle;
        _containerId = containerId;
        _headerHandle = WindowClassApi.CreateWindowEx(
            WS_EX_TOOLWINDOW | NativeChrome.NoActivateStyle,
            WindowClassName, "Tessel", NativeBackdrop.FramedWindowStyle,
            area.X, area.Y, area.Width, RowHeight,
            nint.Zero, nint.Zero, ModuleApi.GetModuleHandle(null), nint.Zero);

        if (_headerHandle == nint.Zero)
            throw new InvalidOperationException($"Could not create Tessel header. Win32 error: {Marshal.GetLastPInvokeError()}");

        _backdrop = new ComposedBackdrop(_headerHandle);
        Headers.Add(_headerHandle, this);
    }

    private int Update(LayoutArea area, IReadOnlyList<WindowMetadata> windows,
        nint activeHandle, LayoutType layoutType, int? reservedHeight, uint dpi, LayoutDockEdges dockedEdges,
        bool showDivider, (uint? Light, uint? Dark) indicatorColors)
    {
        bool contentsChanged = _layoutType != layoutType || _activeHandle != activeHandle ||
            _showDivider != showDivider || _indicatorColors != indicatorColors;
        bool cornersChanged = _dockedEdges != dockedEdges;
        _showDivider = showDivider;
        _indicatorColors = indicatorColors;
        _dockedEdges = dockedEdges;
        if (!_windows.SequenceEqual(windows))
        {
            _windows = [.. windows];
            _hoveredIndex = -1;
            _closeHovered = false;
            _pressedCloseHandle = nint.Zero;
            contentsChanged = true;
        }

        bool moved = _x != area.X || _y != area.Y || _width != area.Width;
        int previousHeight = _headerHeight;
        uint previousDpi = _dpi;
        _layoutType = layoutType;
        _activeHandle = activeHandle;

        _x = area.X;
        _y = area.Y;
        _width = area.Width;

        if (dpi == 0)
            dpi = _pendingDpi != 0 ? _pendingDpi : MonitorEnumerator.GetDpi(_monitorHandle);
        _pendingDpi = 0;
        UpdateHeight(dpi, reservedHeight, area.Height);
        if (_headerHeight == 0)
        {
            Hide();
            return 0;
        }
        moved |= _headerHeight != previousHeight || _dpi != previousDpi;
        bool showing = !WindowApi.IsWindowVisible(_headerHandle);
        if (moved || showing)
            Position();
        if (moved || showing || cornersChanged)
            UpdateCorners();

        if (_headerHeight > 0 && (contentsChanged || moved || showing || cornersChanged))
            WindowApi.InvalidateRect(_headerHandle, nint.Zero, false);

        return _headerHeight;
    }

    private void UpdateHeight(uint dpi, int? reservedHeight, int availableHeight)
    {
        _dpi = dpi;
        int rows = _layoutType == LayoutType.Stacking ? _windows.Count : 1;
        _headerHeight = reservedHeight ?? rows * Math.Min(GetRowHeight(dpi), (availableHeight - 1) / rows);
    }

    public static void BringToFront(nint monitorHandle, Guid containerId = default) =>
        FindHeader(monitorHandle, containerId)?.BringToFront();

    public static IReadOnlyList<(nint Monitor, Guid Container, LayoutType Layout, WindowPlacement Tile)> CaptureDropTabs()
    {
        var tabs = new List<(nint, Guid, LayoutType, WindowPlacement)>();
        foreach (var header in Headers.Values)
        {
            if (!WindowApi.IsWindowVisible(header._headerHandle))
                continue;

            for (int index = 0; index < header._windows.Count; index++)
            {
                var cell = header.CellBounds(index);
                tabs.Add((header._monitorHandle, header._containerId, header._layoutType, new WindowPlacement(
                    header._windows[index].Handle, header._x + cell.Left, header._y + cell.Top,
                    cell.Right - cell.Left, cell.Bottom - cell.Top)));
            }
        }

        return tabs;
    }

    public static void SetForegroundOverlay(nint hwnd)
    {
        if (_foregroundOverlay == hwnd)
            return;

        _foregroundOverlay = hwnd;
        nint insertAfter = HeaderInsertAfter();
        foreach (var header in Headers.Values)
        {
            if (WindowApi.IsWindowVisible(header._headerHandle))
                WindowApi.SetWindowPos(header._headerHandle, insertAfter, 0, 0, 0, 0,
                    SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
    }

    private static nint HeaderInsertAfter()
    {
        nint overlay = _foregroundOverlay;
        if (overlay != nint.Zero && overlay == WindowFocuser.GetForegroundHandle() &&
            (WindowApi.GetWindowLongPtr(overlay, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) == 0)
            return overlay;

        // HWND_TOP raises the header within the non-topmost band.
        return nint.Zero;
    }

    private void Position()
    {
        WindowApi.SetWindowPos(
            _headerHandle,
            HeaderInsertAfter(),
            _x,
            _y,
            _width,
            _headerHeight,
            SWP_NOACTIVATE | SWP_SHOWWINDOW
        );

    }

    private void BringToFront() =>
        WindowApi.SetWindowPos(_headerHandle, HeaderInsertAfter(), 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

    private void UpdateCorners()
    {
        if (_width <= 0 || _headerHeight <= 0)
            return;

        const LayoutDockEdges horizontal = LayoutDockEdges.Left | LayoutDockEdges.Right;
        const LayoutDockEdges vertical = LayoutDockEdges.Top | LayoutDockEdges.Bottom;
        bool square = NativeChrome.Colors.HighContrast || (_dockedEdges & horizontal) == horizontal ||
            (_dockedEdges & vertical) == vertical;
        int radius = square ? 0 :
            Math.Min(NativeChrome.Scale(8, _dpi), Math.Min(_width, _headerHeight) / 2);
        var mask = (_width, _headerHeight, radius, _dockedEdges);
        if (_cornerMask == mask)
            return;

        bool roundedByDwm = radius > 0 && _dockedEdges == LayoutDockEdges.None;
        bool appliedPreference = _backdrop.SetSystemCorners(roundedByDwm);
        int regionRadius = roundedByDwm && appliedPreference ? 0 : radius;
        if ((regionRadius == 0 && !_hasWindowRegion) ||
            NativeChrome.TryApplyRoundedRegion(_headerHandle, _width, _headerHeight, regionRadius, _dockedEdges))
        {
            _hasWindowRegion = regionRadius > 0;
            _cornerMask = mask;
        }
        else
            Console.Error.WriteLine("Could not apply rounded corners to the layout header.");
    }

    public static void Hide(nint monitorHandle, Guid containerId = default) =>
        FindHeader(monitorHandle, containerId)?.Hide();

    public static void RemoveUnusedHeaders(IReadOnlySet<(nint Monitor, Guid Container)> used, bool retain = false)
    {
        foreach (var header in Headers.Values.ToArray())
        {
            if (used.Contains((header._monitorHandle, header._containerId)))
                continue;

            if (retain)
                header.Hide();
            else
                header.Destroy();
        }
    }

    private void Hide()
    {
        _hoveredIndex = -1;
        _closeHovered = false;
        _pressedCloseHandle = nint.Zero;
        if (WindowApi.IsWindowVisible(_headerHandle))
            WindowApi.ShowWindow(_headerHandle, SW_HIDE);
    }

    public static void RemoveHeadersNotOnMonitors(IReadOnlySet<nint> monitorHandles)
    {
        foreach (var header in Headers.Values.ToArray())
        {
            if (monitorHandles.Contains(header._monitorHandle))
                continue;

            header.Destroy();
        }
    }

    private void Destroy()
    {
        Hide();
        if (WindowApi.DestroyWindow(_headerHandle))
        {
            _backdrop.Dispose();
            if (_closeFont != 0)
                GdiApi.DeleteObject(_closeFont);
            Headers.Remove(_headerHandle);
        }
        else
            Console.Error.WriteLine($"Could not destroy Tessel header. Win32 error: {Marshal.GetLastPInvokeError()}");
    }

    private static void EnsureWindowClass()
    {
        if (_classRegistered)
            return;

        NativeWindowClassRegistrar.Register(
            WindowClassName,
            Marshal.GetFunctionPointerForDelegate(Procedure)
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

        if (!Headers.TryGetValue(hwnd, out var header))
            return WindowClassApi.DefWindowProc(hwnd, message, wParam, lParam);

        switch (message)
        {
            case WM_PAINT:
                header._backdrop.Paint(header._dpi, header._width, header._headerHeight, header.PaintHeader);
                return nint.Zero;

            case WM_ERASEBKGND:
                return 1;

            case NativeChrome.ThemeChanged:
            case NativeChrome.ColorizationChanged:
            case NativeChrome.SystemColorsChanged:
            case NativeChrome.SettingsChanged:
                NativeChrome.ThemeChangedFor(hwnd);
                header._backdrop.Refresh();
                header.UpdateCorners();
                if (message == NativeChrome.SettingsChanged && WindowFocuser.IsVisible(hwnd))
                    NativeChrome.QueueLayoutRefresh(hwnd);
                return nint.Zero;

            case NativeBackdrop.CompositionChanged:
                header._backdrop.Refresh();
                WindowApi.InvalidateRect(hwnd, nint.Zero, false);
                return nint.Zero;

            case NativeChrome.DpiChanged:
                header._pendingDpi = NativeChrome.DpiFromMessage(wParam);
                NativeChrome.QueueLayoutRefresh(hwnd);
                return nint.Zero;

            case NativeChrome.RefreshLayout:
                if (WindowFocuser.IsVisible(hwnd) &&
                    header._dpi != (header._pendingDpi != 0 ? header._pendingDpi : MonitorEnumerator.GetDpi(header._monitorHandle)))
                    NativeChrome.RequestLayout();
                return nint.Zero;

            case NativeChrome.MouseMove:
                int hoveredIndex = header.WindowAt(lParam);
                bool closeHovered = hoveredIndex >= 0 &&
                    NativeChrome.Contains(header.CloseBounds(header.CellBounds(hoveredIndex)), lParam);
                if (hoveredIndex != header._hoveredIndex || closeHovered != header._closeHovered)
                {
                    NativeChrome.TrackMouseLeave(hwnd);
                    header._hoveredIndex = hoveredIndex;
                    header._closeHovered = closeHovered;
                    WindowApi.InvalidateRect(hwnd, nint.Zero, false);
                }
                return nint.Zero;

            case NativeChrome.MouseLeave:
                header._hoveredIndex = -1;
                header._closeHovered = false;
                header._pressedCloseHandle = nint.Zero;
                WindowApi.InvalidateRect(hwnd, nint.Zero, false);
                return nint.Zero;

            case WM_LBUTTONDOWN:
                int pressedIndex = header.WindowAt(lParam);
                header._pressedCloseHandle = pressedIndex >= 0 &&
                    NativeChrome.Contains(header.CloseBounds(header.CellBounds(pressedIndex)), lParam)
                    ? header._windows[pressedIndex].Handle : nint.Zero;
                return nint.Zero;

            case WM_LBUTTONUP:
                nint pressedCloseHandle = header._pressedCloseHandle;
                header._pressedCloseHandle = nint.Zero;
                int index = header.WindowAt(lParam);
                if (index >= 0)
                {
                    nint handle = header._windows[index].Handle;
                    if (pressedCloseHandle == handle &&
                        NativeChrome.Contains(header.CloseBounds(header.CellBounds(index)), lParam))
                    {
                        if (!WindowApi.PostMessage(handle, WM_CLOSE, 0, nint.Zero))
                            Console.Error.WriteLine($"Could not request window close. Win32 error: {Marshal.GetLastPInvokeError()}");
                    }
                    else if (pressedCloseHandle == nint.Zero)
                    {
                        WindowSelected?.Invoke(handle);
                    }
                }
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

    private NativeWindowRect CellBounds(int index)
    {
        int rowHeight = _layoutType == LayoutType.Stacking ? _headerHeight / _windows.Count : _headerHeight;
        if (_layoutType == LayoutType.Stacking)
            return NativeChrome.Rect(0, index * rowHeight, _width, (index + 1) * rowHeight);

        int left = (int)((long)index * _width / _windows.Count);
        int right = (int)((long)(index + 1) * _width / _windows.Count);
        return NativeChrome.Rect(left, 0, right, rowHeight);
    }

    private int WindowAt(nint mousePosition) =>
        WindowAt(unchecked((short)mousePosition), unchecked((short)(mousePosition >> 16)));

    private int WindowAt(int x, int y)
    {
        for (int index = 0; index < _windows.Count; index++)
        {
            var cell = CellBounds(index);
            if (x >= cell.Left && x < cell.Right && y >= cell.Top && y < cell.Bottom)
                return index;
        }

        return -1;
    }

    private NativeWindowRect CloseBounds(NativeWindowRect cell)
    {
        int inset = NativeChrome.Scale(4, _dpi) + NativeChrome.Scale(3, _dpi);
        int side = Math.Min(NativeChrome.Scale(24, _dpi), cell.Bottom - cell.Top - inset * 2);
        if (side <= 0 || cell.Right - cell.Left < side + inset * 2)
            return default;

        int top = (cell.Top + cell.Bottom - side) / 2;
        int right = cell.Right - inset;
        return NativeChrome.Rect(right - side, top, right, top + side);
    }

    private void PaintHeader(nint hdc, NativeChrome.Palette colors)
    {
        int line = NativeChrome.Scale(1, _dpi);
        int inset = NativeChrome.Scale(4, _dpi);
        int textInset = NativeChrome.Scale(12, _dpi);
        int corner = colors.HighContrast ? 0 : NativeChrome.Scale(6, _dpi);

        if (_showDivider && (_dockedEdges & LayoutDockEdges.Top) != 0)
            NativeChrome.Fill(hdc,
                NativeChrome.Rect(0, _headerHeight - line, _width, _headerHeight), colors.Divider);

        for (int i = 0; i < _windows.Count; i++)
        {
            var window = _windows[i];
            var cell = CellBounds(i);
            bool active = window.Handle == _activeHandle;
            var tab = NativeChrome.Rect(
                cell.Left + inset, cell.Top + inset, cell.Right - inset, cell.Bottom - inset);

            if (active || i == _hoveredIndex)
                NativeChrome.Fill(hdc, tab, active ? colors.Selected : colors.Hover, corner);

            if (active && tab.Right - tab.Left > textInset * 2)
            {
                int centerY = (tab.Top + tab.Bottom) / 2;
                int halfHeight = Math.Min(NativeChrome.Scale(8, _dpi), (tab.Bottom - tab.Top) / 2);
                int thickness = NativeChrome.Scale(3, _dpi);
                NativeChrome.FillRounded(hdc,
                    NativeChrome.Rect(tab.Left + inset, centerY - halfHeight,
                        tab.Left + inset + thickness, centerY + halfHeight),
                    colors.Indicator(_indicatorColors), colors.HighContrast ? 0 : thickness / 2.0);
            }

            string title = string.IsNullOrWhiteSpace(window.Title) ? "Untitled window" : window.Title;
            var close = CloseBounds(cell);
            int titleRight = close.Right > close.Left ? close.Left - inset : tab.Right - inset;
            NativeChrome.Text(hdc, title,
                NativeChrome.Rect(tab.Left + textInset, tab.Top, titleRight, tab.Bottom),
                active ? colors.SelectedText : colors.SecondaryText);
            bool closeHovered = i == _hoveredIndex && _closeHovered;
            uint closeText = active ? colors.SelectedText : colors.SecondaryText;
            if (closeHovered)
            {
                uint closeFill = colors.Dark ? 0x004C4C4Cu : 0x00DDD9D6u;
                if (colors.HighContrast)
                {
                    closeFill = active ? colors.SelectedText : colors.Selected;
                    closeText = active ? colors.Selected : colors.SelectedText;
                }
                else
                {
                    closeText = colors.Text;
                }
                NativeChrome.FillRounded(hdc, close, closeFill, corner);
            }
            DrawCloseIcon(hdc, close, closeText);
        }
    }

    private void DrawCloseIcon(nint dc, NativeWindowRect bounds, uint color)
    {
        if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top)
            return;

        if (_closeFontDpi != _dpi)
        {
            if (_closeFont != 0)
                GdiApi.DeleteObject(_closeFont);
            _closeFont = NativeChrome.CreateFont(_dpi, "Segoe Fluent Icons", 8);
            _closeFontDpi = _dpi;
        }

        nint previous = _closeFont == 0 ? 0 : GdiApi.SelectObject(dc, _closeFont);
        if (previous == 0 || previous == -1)
        {
            NativeChrome.Text(dc, "×", bounds, color, NativeChrome.Center);
            return;
        }

        try
        {
            NativeChrome.Text(dc, "\uE8BB", bounds, color, NativeChrome.Center, symbolSize: 8); // ChromeClose
        }
        finally
        {
            GdiApi.SelectObject(dc, previous);
        }
    }
}
